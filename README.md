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

Fable.Giraffe's major version tracks the Fable compiler it targets: the 5.x
line is built with and requires Fable 5.

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

An identical `/ping` handler (returns `pong`) on every target, driven with
[oha](https://github.com/hatoo/oha): 10,000 requests at 100 concurrent
connections over loopback, with **no per-request logging on any target**. The
.NET row is the original Giraffe on ASP.NET Core / Kestrel, included as a
reference point.

| Target | Requests/sec | Avg latency | P99 latency |
|---|---|---|---|
| .NET (reference) | ~321,000 | 0.29 ms | 1.33 ms |
| Erlang/BEAM (Cowboy) | ~217,000 | 0.44 ms | 1.71 ms |
| JavaScript (Node) | ~63,000 | 1.56 ms | 3.24 ms |
| Python (uvicorn, 1 worker) | ~14,000 | 6.95 ms | 15.22 ms |

These numbers are machine-dependent and only meaningful relative to one another;
throughput at the top of the table is noisy because the framework outruns the
loopback/oha harness driving it. The harness lives in [`perf/`](perf/) — reproduce
with `just bench` (or `just bench python js` for a subset).

Every target must share the same logging configuration for the comparison to
mean anything: an earlier version of this table ran with logging enabled on .NET
but not BEAM, which made .NET look slower than BEAM. The current run turns
per-request logging off everywhere.
