# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Fable.Giraffe is a port of the Giraffe F# web framework to Python via Fable. It compiles F# source code to Python using the Fable compiler, producing a fully ASGI-compatible web framework that runs on Python servers like uvicorn. The runtime depends on Starlette (ASGI) and structlog (logging).

## Build Commands

The project uses a Justfile for build orchestration:

```bash
just              # List all available targets
just build        # Clean + compile F# library to Python (output: build/lib/)
just test         # Run the shared behavioral suite on all 3 targets (Python, JS, BEAM)
just test-native  # Type-check the test projects on .NET (compile smoke only)
just test-python  # Compile & run the suite on the Python target
just test-js      # Compile & run the suite on the JavaScript/Node target
just test-beam    # Compile & run the suite on the Erlang/BEAM target
just app          # Build + start example app with uvicorn on port 8080
just pack         # Build + create NuGet package
just format       # Format all F# code with fantomas
just setup        # Restore dotnet tools + uv sync
```

For local Fable development (using a local Fable compiler checkout):

```bash
just dev=true build
just dev=true test
```

### Running tests manually

```bash
# All three targets
just test

# One target at a time
just test-python
just test-js
just test-beam
```

### Prerequisites

- .NET SDK 8+, Python >= 3.12, uv for Python dependency management
- Install dotnet tools: `dotnet tool restore`
- Install Python deps: `uv sync`

## Architecture

### Core Type System

The framework is built on three types in `src/Core.fs`:

```fsharp
type HttpFuncResult = Task<HttpContext option>
type HttpFunc = HttpContext -> HttpFuncResult
type HttpHandler = HttpFunc -> HttpFunc
```

`HttpHandler` is a higher-order function composed with the `>=>` (fish) operator. Returning `Some ctx` continues the pipeline; returning `None` skips to the next handler. This is the same pattern as the original Giraffe framework.

### Key Source Files (src/)

- **Core.fs** - Handler composition (`>=>`), HTTP verb filters (GET, POST, etc.), `choose`, `earlyReturn`
- **HttpContext.fs** - Wraps ASGI scope/receive/send into an F# HTTP abstraction
- **Routing.fs** - Route matching (`route`, `routeCi`, `routeCix`, `subRoute`), parameter extraction
- **FormatExpressions.fs** - Route parameter parsing (typed format strings like `%s`, `%i`, `%O`)
- **Negotiation.fs** - Content negotiation based on Accept headers
- **Middleware.fs** - `GiraffeMiddleware` that bridges handlers into the ASGI pipeline
- **WebHost.fs** - `WebHostBuilder` for configuring the application, logging, and services
- **Json.fs** - Custom JSON serialization wrapping Fable.Python.Json with underscore-stripping for Fable 5 compatibility
- **Remoting.fs** - RPC-style remoting via reflection over F# record types (shared; Python + JS)
- **StaticFiles.fs** - Static file serving via Starlette

### Remoting

`src/Remoting.fs` is shared across **all three** targets (Python, JS, BEAM). It reflects over a record
of `... -> Async<'T>` fields and generates one sub-route per field under `/{ApiName}`. Only three
small primitives are target-specific, and they sit behind `PlatformHelpers`:

- `startAsTask` — Async->Task bridge. Direct on Python; JS routes through `Async.StartAsPromise`
  because fable-library-js has no `startAsTask` (`Async.StartAsTask` fails to link at runtime); on
  BEAM it is the identity (`unbox`), since `task` is a CPS alias for `Async` there.
- `isJsonObject` / `getJsonMember` — test a decoded JSON value for object-ness and read a member by
  name (`dict` on Python, plain object on JS, an Erlang `map` on BEAM). On BEAM the decoded map is
  keyed by the mangled record-field atom, not the F# name, so `getJsonMember` first maps the
  reflection name through `toWireKey` (a reproduction of Fable's `sanitizeFieldName`: snake_case,
  lowercased, trailing `_` for lowercase-first names). See the Fable follow-up gap below.

Argument reconstruction itself (`RemotingHelpers.convertJsonValue`) is shared and recursive: it
rebuilds records field-by-field via `FSharpValue.MakeRecord` and recurses through nested records and
`'T list`. **Unions, options and maps are passed through unconverted** — they will reach the handler
as raw decoded values.

Failure handling: a body that is not a JSON array, or an argument count that does not match the
method, answers **400**; an exception raised by an API method answers **500** with
`{"error": "Internal server error"}` and does not leak the exception. `Remoting.withErrorHandler`
replaces that default with an `exn -> HttpHandler`. The handler-exception path uses `Async.Catch`
rather than `try/with` around a `let!`, which is the construct Fable compiles consistently.

Note the **JSON wire format is not identical across backends**: Fable lowercases record field names
on Python (`{"description": ...}`) and snake_case-lowercases them on BEAM (`{"description": ...}`,
`{"first_name": ...}`) but preserves them on JS (`{"Description": ...}`), so a JS client and a
Python/BEAM server do not currently interoperate. Tests build expectations via `serialize` rather
than literals for this reason.

**BEAM was unblocked by Fable 5.13.0** (`fix(beam)` #4849 made reflection value access agree with
record *and union* codegen — `PropertyInfo.GetValue` / `FSharpValue.MakeRecord` / `MakeUnion` no
longer `{badkey,...}`). Remoting now runs on all three targets. One BEAM-specific note remains:

- Reflection reports the pristine F# field name, not the record-map key, so `getJsonMember`
  reproduces `sanitizeFieldName` via `toWireKey` (see above). The Fable team **declined** to change
  the BEAM reflection surface or wire format (neither is needed for reflection correctness), so
  `toWireKey` is the **sanctioned** integration point — not a temporary shim to delete. `sanitizeFieldName`
  is stable; if it ever changes, the wire key is treated as contract. Background in
  `../Fable/BEAM-RECORD-FIELD-NAME-MANGLING-PROMPT.md`.

The BEAM `testSequenced` workaround is **gone** (issue #54 closed): Quill 0.5.1 (Unicode output on
BEAM, Scriptorium #14) and Nib 0.4.1 (char-level diffs on BEAM, #15) fixed the garbled-diff failures,
so `test/beam/Main.fs` runs the three suites with plain parallel `runTests` like the other targets.

**Python tripwire on the next `fable-library-py` bump.** The Fable team is fixing Python reflection to
report the *pristine* F# field name (like BEAM) while keeping the snake_case runtime slot
(`PYTHON-RECORD-REFLECTION-FIELD-NAME-PROMPT.md`). When that lands, `convertJsonValue`'s
`getJsonMember value f.Name` on Python will look up `FirstName` against a wire still keyed
`first_name` (`giraffeDefault` reads `__slots__`) → reconstruction breaks. On that bump, add a Python
wire-key mapping mirroring BEAM's `toWireKey` (pristine → snake_case slot), or serialize on the
pristine name. The Python remoting tests use single-word PascalCase fields, which mangle to
themselves, so they will *not* catch it — add a multi-word field first.

### Logging

All three backends wire `Fable.Logging` through the shared `src/Logging.fs`, opt-in per target:
`UseStructlog()` (Python), `UseConsoleLogging()` (JS), `UseBeamLogging()` (BEAM). Python and BEAM
also emit a per-request access log gated on `LogLevel.Debug`; JS does not.

**BEAM: objects cannot cross the Cowboy request boundary.** Cowboy spawns a fresh process per
request, and Fable compiles a class with mutable fields to a *process-dictionary ref* — so any
such object built in the builder process reads back as `undefined` inside the request process
(`{badmap,undefined}`). This rules out passing an `ILogger` (or any Fable.Logging object) through
Cowboy handler state. `GiraffeHandler` therefore receives `accessLogEnabled: bool` — an
`IsEnabled LogLevel.Debug` evaluated once in `WebHost.Build`, which is sound because `Build`
starts the listener and no `ConfigureLogging` can follow it — and emits via OTP's global `logger`,
where `Fable.Logging.Beam`'s provider sends its output anyway. The cost is that a *custom*
provider won't see the access log.

The same constraint breaks `ctx.GetService` on BEAM (`ServiceCollection` is equally ref-backed);
see FOLLOWUPS.md. Nothing in the suite covers it, since the tests bypass `GiraffeHandler`.

### Build System

- `Justfile` - Build targets (replaces the old FAKE-based Build.fs)
- Uses Fable 5.13.0 for F# to Python, JavaScript and BEAM compilation
- Uses uv for Python dependency management

### Compilation Flow

F# source (`src/`) -> Fable compiler (F# to Python) -> Python output (`build/lib/`)
Tests (`test/`) -> compiled per target to `build/tests-py/`, `build/test-js/`, `build/tests-beam/`

### Test Structure

One behavioral suite in `test/shared/` (`HandlerTests.fs`, `RoutingTests.fs`) is compiled into three
per-target projects — `test/python`, `test/js`, `test/beam` — each supplying its own `TestContext.fs`
(a `TestContext.create` factory building an isolated context without a real server) and a thin
`Main.fs` entry point. `RemotingTests.fs` runs on all three targets as of Fable 5.13.0 (see the
Remoting note below for the remaining BEAM-specific caveat: `toWireKey`).

Tests are written with [Scriptorium](https://github.com/fable-hub/Scriptorium) — Quill for the test
DSL and runner, Nib for assertions — both of which compile to all three targets. `test/shared/Helpers.fs`
holds the remaining glue: `next`, `getContentType`, and the `toAsync` Task->Async bridge (the suite's
only `#if`, since on BEAM `task` is a CPS alias for Async and the conversion is the identity).

Known per-target divergences are marked with Quill's `skipIfBeam` / `skipIfJavaScript` configurers
colocated with the test, each carrying a comment explaining the gap. Quill has no skip-reason field,
so the comment is the record. Quill runs tests with `Async.Parallel`, and on BEAM each test executes
in a spawned, heap-isolated process — so **all per-test setup must live inside the `task { }` block**,
not in the thunk around it, or BEAM-side refs created in the parent are invisible to the test.

There is no pure-.NET behavioral run: `src` is Fable-only bindings, so `just test-native` is a
compile smoke test.

### Fable 5 Workarounds

The earlier Fable 5 alpha/RC code-generation bugs (missing `await` in if/match and ternary branches) are fixed as of Fable 5.4.0. The `task { ... }` computation expressions in Core.fs and Routing.fs are now idiomatic Giraffe style rather than workarounds.

`Json.fs` still strips trailing underscores from serialized field names: Fable mangles identifiers that collide with Python keywords (e.g. `type` -> `type_`), so stripping restores the intended JSON key. Plain record fields are no longer mangled in 5.4, so for them this is a no-op.

## Code Style

F# formatting uses Fantomas with settings in `.editorconfig`:

- Max line length: 140
- Stroustrup-style brackets
- Multiline block brackets on same column
