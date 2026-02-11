# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Fable.Giraffe is a port of the Giraffe F# web framework to Python via Fable. It compiles F# source code to Python using the Fable compiler, producing a fully ASGI-compatible web framework that runs on Python servers like uvicorn. The runtime depends on Starlette (ASGI) and structlog (logging).

## Build Commands

The project uses a Justfile for build orchestration:

```bash
just              # List all available targets
just build        # Clean + compile F# library to Python (output: build/lib/)
just test         # Build + run native F# tests + compile & run pytest
just test-native  # Run native F# tests only
just test-python  # Build + compile & run Python tests only
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
# Full pipeline (native + Python)
just test

# Just the Python tests (after building)
uv run python -m pytest build/tests

# Just native F# tests
dotnet run --project test
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
- Uses Fable 5.0.0-alpha.23 for F# to Python compilation
- Uses uv for Python dependency management

### Compilation Flow

F# source (`src/`) -> Fable compiler (F# to Python) -> Python output (`build/lib/`)
Tests (`test/`) -> compiled to `build/tests/` -> run with pytest

### Test Structure

Tests in `test/` run in two modes: native F# (xUnit via `dotnet run`) and compiled Python (pytest). `test/Helpers.fs` provides `HttpTestContext` for creating isolated test contexts without a real server.

### Fable 5 Workarounds

Due to Fable 5 alpha bugs (documented in `FABLE_V5_BUGS.md`), handlers that return `Task<T>` by passing through if/match branches must use explicit `task { return! ... }` instead of direct returns. See Core.fs and Routing.fs for examples.

## Code Style

F# formatting uses Fantomas with settings in `.editorconfig`:

- Max line length: 140
- Stroustrup-style brackets
- Multiline block brackets on same column
