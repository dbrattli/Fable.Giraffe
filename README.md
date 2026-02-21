# Fable.Giraffe

[![Build and Test](https://github.com/dbrattli/Fable.Giraffe/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/dbrattli/Fable.Giraffe/actions/workflows/build-and-test.yml)
[![Nuget](https://img.shields.io/nuget/vpre/Fable.Giraffe)](https://www.nuget.org/packages/Fable.Giraffe/)

Fable.Giraffe is a port of the
[Giraffe](https://github.com/giraffe-fsharp/Giraffe) F# web framework to
[Fable](https://github.com/fable-compiler/Fable/). Write your web application
once in F# and run it on Python (ASGI/uvicorn) or Erlang/BEAM (Cowboy).

## Example

```fsharp
let webApp =
    choose [
        route "/ping" >=> text "pong"
        route "/json" >=> json {| name = "Dag"; age = 53 |}
    ]

let app =
    WebHostBuilder()
        .Configure(fun app -> app.UseGiraffe(webApp))
        .Build()
```

## Prerequisites

- .NET SDK 8+
- Python >= 3.12 with [uv](https://github.com/astral-sh/uv)
- Erlang/OTP 28+ (for BEAM target)

## Build

```console
just setup     # restore dotnet tools + uv sync
just build     # compile F# library to Python (output: build/lib/)
just build-beam  # compile F# library to Erlang (output: build/beam/)
```

For local Fable development (using a local Fable compiler checkout):

```console
just dev=true build
```

## Running

### Python (ASGI)

```console
just app
```

This compiles the example app and starts it with uvicorn on port 8080.

### Erlang/BEAM (Cowboy)

```console
just app-beam
```

This compiles the example app to Erlang, builds with rebar3, and starts a Cowboy server on port 8080.

## Testing

```console
just test          # native F# tests + compiled Python tests
just test-native   # native F# tests only (xUnit)
just test-python   # compiled Python tests only (pytest)
```

## Benchmarks

Simple `/ping` endpoint returning "pong", 10,000 requests with 100 concurrent
connections (oha):

| Metric | BEAM | .NET | Python |
|---|---|---|---|
| Requests/sec | 124,256 | 70,375 | 4,006 |
| Avg latency | 0.79 ms | 1.40 ms | 24.9 ms |
| P99 latency | 2.49 ms | 3.50 ms | 34.2 ms |

BEAM: Erlang/OTP 28, Cowboy. .NET: Giraffe on ASP.NET Core. Python: uvicorn, 1 worker.
