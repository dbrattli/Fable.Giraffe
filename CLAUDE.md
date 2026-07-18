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
- **Remoting.fs** - RPC-style remoting via reflection over F# record types
- **StaticFiles.fs** - Static file serving via Starlette

### Build System

- `Justfile` - Build targets (replaces the old FAKE-based Build.fs)
- Uses Fable 5.11.0 for F# to Python, JavaScript and BEAM compilation
- Uses uv for Python dependency management

### Compilation Flow

F# source (`src/`) -> Fable compiler (F# to Python) -> Python output (`build/lib/`)
Tests (`test/`) -> compiled per target to `build/tests-py/`, `build/test-js/`, `build/tests-beam/`

### Test Structure

One behavioral suite in `test/shared/` (`HandlerTests.fs`, `RoutingTests.fs`) is compiled into three
per-target projects — `test/python`, `test/js`, `test/beam` — each supplying its own `TestContext.fs`
(a `TestContext.create` factory building an isolated context without a real server) and a thin
`Main.fs` entry point. `RemotingTests.fs` is Python-only.

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
