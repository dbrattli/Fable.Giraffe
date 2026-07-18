# Fable.Giraffe

[![Build and Test](https://github.com/dbrattli/Fable.Giraffe/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/dbrattli/Fable.Giraffe/actions/workflows/build-and-test.yml)
[![Nuget](https://img.shields.io/nuget/vpre/Fable.Giraffe)](https://www.nuget.org/packages/Fable.Giraffe/)

Fable.Giraffe is a port of the
[Giraffe](https://github.com/giraffe-fsharp/Giraffe) F# web framework to
[Fable](https://github.com/fable-compiler/Fable/). Write your web application
once in F# and run it on three runtimes:

| Target | Runtime | Server |
|---|---|---|
| Python | ASGI | uvicorn (Starlette) |
| JavaScript | Node.js | built-in `node:http`, or mounted as `connect`/`express` middleware |
| Erlang/BEAM | OTP | Cowboy |

## Example

The handler pipeline is identical on every target:

```fsharp
let webApp =
    choose [
        route "/ping" >=> text "pong"
        route "/json" >=> json {| name = "Dag"; age = 53 |}
    ]
```

Only how you start the host differs:

```fsharp
// Python — returns an ASGI app for uvicorn
let app =
    WebHostBuilder()
        .Configure(fun app -> app.UseGiraffe(webApp))
        .Build()

// JavaScript — starts a node:http server
WebHostBuilder()
    .Configure(fun app -> app.UseGiraffe(webApp))
    .Run(8080)

// Erlang/BEAM — starts Cowboy
let start () =
    WebHostBuilder()
        .Configure(fun app -> app.UseGiraffe(webApp))
        .Build(8080)
```

## Prerequisites

- .NET SDK 8+
- Python >= 3.12 with [uv](https://github.com/astral-sh/uv)
- Node.js 20+ (JavaScript target)
- Erlang/OTP 27+ with rebar3 (BEAM target)

## Build

```console
just setup       # restore dotnet tools + uv sync
just build       # F# -> Python   (output: build/lib/)
just build-js    # F# -> JavaScript (output: build/js/)
just build-beam  # F# -> Erlang   (output: build/apps/giraffe/)
```

For local Fable development (using a local Fable compiler checkout in `../Fable`):

```console
just dev=true build
```

## Running

Each of these compiles the example app in `app/` and serves it on port 8080.

```console
just app       # Python — uvicorn
just app-js    # JavaScript — node:http
just app-beam  # Erlang/BEAM — Cowboy
```

## Testing

One shared behavioral suite in `test/shared/` runs on all three targets:

```console
just test          # all three targets
just test-python   # Python target
just test-js       # JavaScript target
just test-beam     # Erlang/BEAM target
just test-native   # type-check the test projects on .NET (compile smoke only)
```

Tests are written with [Scriptorium](https://github.com/fable-hub/Scriptorium) —
Quill for the test DSL and runner, Nib for assertions — both of which compile to
every target. Per-target divergences are marked with `skipIfBeam` /
`skipIfJavaScript` next to the test, each carrying a comment explaining the gap,
so they show up as skips rather than silently disappearing.

There is no pure-.NET behavioral run: `src` is Fable-only bindings, so
`test-native` is a compile smoke test.

## Benchmarks

Simple `/ping` endpoint returning "pong", 10,000 requests with 100 concurrent
connections (oha):

| Metric | BEAM | .NET | Python |
|---|---|---|---|
| Requests/sec | 124,256 | 70,375 | 4,006 |
| Avg latency | 0.79 ms | 1.40 ms | 24.9 ms |
| P99 latency | 2.49 ms | 3.50 ms | 34.2 ms |

BEAM: Erlang/OTP 28, Cowboy. .NET: Giraffe on ASP.NET Core. Python: uvicorn, 1 worker.
The JavaScript/Node target has not been benchmarked yet.
