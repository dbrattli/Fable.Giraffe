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
- **Json.fs** (per target) - Thin binding to Fable.TypedJson; see "JSON" below
- **Remoting.fs** - RPC-style remoting via reflection over F# record types (shared; Python + JS)
- **StaticFiles.fs** - Static file serving via Starlette

### JSON

All three backends serialize through **Fable.TypedJson** (`../Fable.TypedJson`, currently a
`ProjectReference` pending an rc.2 release). Each `src/<target>/Json.fs` is a thin binding to
that backend's TypedJson shim exposing `codec<'T>`, `codecFor : System.Type -> TypedJson<obj>`,
`serialize`, `serializeAs`, `deserialize` (parse to the backend-native value) and
`tryDeserialize`.

Two properties follow, and both are the reason for the dependency:

- **Schema and wire format cannot disagree.** TypedJson produces a type's decoder, encoder and
  JSON Schema from one walk, with that as a stated invariant. An OpenAPI document generated
  from it cannot describe a field the serializer does not emit — a guarantee the ASP.NET stack
  does not have, since Swashbuckle and `System.Text.Json` derive their views separately.
- **Field names agree across targets.** The wire is **camelCase** everywhere
  (`CaseRules.LowerFirst`, TypedJson's default, which also means the codec reuses one
  pre-resolved plan instead of rebuilding per call). The old hand-written serializers disagreed:
  the same record reached the wire as `Description` on JS and `description` on Python/BEAM.

**Bytes still differ across backends, and cannot be made identical.** Python's `json.dumps`
adds `", "` / `": "` spacing, and Erlang maps have no insertion order so BEAM emits keys in term
order. Tests compare against `serialize` output rather than literals for this reason.

**Build codecs at composition time, never per request.** A codec costs ~193µs (flat) to ~1ms
(nested) to construct and TypedJson has no memo cache. `Core.json` serializes its value once
where the handler is composed (as `text` already computed its bytes), `bindJson` /
`validateJson` build their codec there, and `Remoting.createRoutes` builds one codec per
argument and one per return type per method. Introducing a per-request `auto<'T> ()` would be a
serious regression.

`Core.validateJson<'T>` is the FastAPI-shaped counterpart to `bindJson`: on a body that does not
fit the type it answers **422** with `{"detail": [{"loc": ..., "msg": ...}]}` from TypedJson's
`Result<'T, FieldError list>`, instead of throwing. Additive — `bindJson` still throws.

### Remoting

`src/Remoting.fs` is shared across **all three** targets (Python, JS, BEAM). It reflects over a record
of `... -> Async<'T>` fields and generates one sub-route per field under `/{ApiName}`. Only one
small primitive is target-specific, and it sits behind `PlatformHelpers`:

- `startAsTask` — Async->Task bridge. Direct on Python; JS routes through `Async.StartAsPromise`
  because fable-library-js has no `startAsTask` (`Async.StartAsTask` fails to link at runtime); on
  BEAM it is the identity (`unbox`), since `task` is a CPS alias for `Async` there.

Argument reconstruction is one **TypedJson codec per argument**, built at composition time from the
argument's reflected `System.Type` via `Json.codecFor`; the response likewise uses a codec built
from the method's return type (the last element of the uncurried signature). This replaced a
hand-rolled recursive walk that rebuilt records field-by-field via `FSharpValue.MakeRecord` — a
second implementation of the type walk the serializer already performs, which passed unions,
options and maps through unconverted and needed a per-backend key mapping (BEAM's `toWireKey`,
`isJsonObject` / `getJsonMember`) to find fields at all. All of that is deleted; the key derivation
is now shared with encode by construction.

A malformed argument answers **400** carrying TypedJson's field-level errors, rather than a generic
"did not match".

Failure handling: a body that is not a JSON array, or an argument count that does not match the
method, answers **400**; an exception raised by an API method answers **500** with
`{"error": "Internal server error"}` and does not leak the exception. `Remoting.withErrorHandler`
replaces that default with an `exn -> HttpHandler`. The handler-exception path uses `Async.Catch`
rather than `try/with` around a `let!`, which is the construct Fable compiles consistently.

Field naming now agrees across backends — see the JSON section above. Tests still build
expectations via `serialize` rather than literals, because *bytes* differ (Python spacing, BEAM key
order) even though names do not.

**BEAM was unblocked by Fable 5.13.0** (`fix(beam)` #4849 made reflection value access agree with
record *and union* codegen — `PropertyInfo.GetValue` / `FSharpValue.MakeRecord` / `MakeUnion` no
longer `{badkey,...}`). Remoting now runs on all three targets. One BEAM-specific note remains:

- Reflection reports the pristine F# field name, not the record-map key. This used to require
  reproducing Fable's `sanitizeFieldName` here as `toWireKey`; TypedJson now absorbs it via
  `Casing.toCanonicalPascal`, which normalises whatever `PropertyInfo.Name` reports on a given
  backend before applying the case rule. `toWireKey` is deleted. Background in
  `../Fable/BEAM-RECORD-FIELD-NAME-MANGLING-PROMPT.md`.

The BEAM `testSequenced` workaround is **gone** (issue #54 closed): Quill 0.5.1 (Unicode output on
BEAM, Scriptorium #14) and Nib 0.4.1 (char-level diffs on BEAM, #15) fixed the garbled-diff failures,
so `test/beam/Main.fs` runs the three suites with plain parallel `runTests` like the other targets.

**The Python reflection tripwire is closed.** The Fable team is changing Python reflection to
report the *pristine* F# field name while keeping the snake_case runtime slot
(`PYTHON-RECORD-REFLECTION-FIELD-NAME-PROMPT.md`). That used to be a live hazard — Remoting looked
fields up by `PropertyInfo.Name` against a wire keyed by slot name, so the bump would have broken
reconstruction silently. TypedJson's `Casing.toCanonicalPascal` normalises either spelling, and
TypedJson's own test suite covers the multi-word case, so the bump is now a non-event here.

### Logging

All three backends wire `Fable.Logging` through the shared `src/Logging.fs`, opt-in per target:
`UseStructlog()` (Python), `UseConsoleLogging()` (JS), `UseBeamLogging()` (BEAM). Python and BEAM
also emit a per-request access log gated on `LogLevel.Debug`; JS does not.

### BEAM: what can cross the Cowboy request boundary

Cowboy spawns a **fresh process per request**, and BEAM processes share nothing. The rule Fable
applies is precise and worth internalising, because it decides what may be put into Cowboy
handler state:

> A class with **any** mutable field compiles to a **process-dictionary ref**; a class without one
> compiles to a **plain map**.

Compare the generated `service_collection_ctor` (has `member val Services with get, set` → calls
`make_ref`) with `string_values_ctor` (no mutable fields → `#{field_strings => ...}`). A ref read
from a *different* process yields `undefined`, and the next field access dies with
`{badmap,undefined}` — a 500 with no obvious connection to the cause.

Portable: records, unions, lists, tuples, funs, and **object expressions** (they compile to
self-contained maps of funs — but only if they close over immutable data, not a ref).
Not portable: any class with mutable fields, `Dictionary`, `ResizeArray`, and therefore every
Fable.Logging logger and `ServiceCollection` itself.

Two consequences, both handled in `src/beam`:

- **Services.** `WebHost.Build` snapshots the collection to a `(string * ServiceDescriptor) list`
  and `GiraffeHandler` rebuilds a `ServiceCollection` from it per request, in the process that
  reads it. This keeps `RequestServices` typed as `ServiceCollection`, so shared `src/Helpers.fs`
  and `src/HttpContextExtensions.fs` are untouched. It makes the *container* portable, not the
  values: a registered service that is itself a mutable class is still a dead ref. On BEAM,
  register immutable values.
- **Logging.** Nothing special is needed as of **Fable.Logging 1.0.0**, which made its loggers
  process-portable (they hold no mutable state and snapshot the factory's providers at creation).
  `Build` just calls `loggerFactory.CreateLogger` for the access log, and the `"Giraffe"` logger
  `Logging.configure` registers travels in the snapshot like any other service. Fable.Giraffe
  briefly carried its own object-expression `PortableLogger` for this; it is gone, and with it the
  limitation that a custom `ILoggerProvider` never saw the access log.

  One ordering constraint follows from that upstream change: a logger snapshots providers and
  level when created, and `AddProvider` no longer reaches back into loggers already handed out. So
  loggers must be created *after* configuration — `Logging.configure` re-registers on every
  `ConfigureLogging` call, and `Build` creates the access logger last. See `src/Logging.fs`.

The shared suite covers `AddSingleton` → `ctx.GetService`, but it bypasses `GiraffeHandler`, so it
does **not** cover the process hop. Verify that by running `just app-beam`.

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
