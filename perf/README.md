# perf

A fair, reproducible throughput benchmark for Fable.Giraffe across all targets.

Each target serves an **identical `/ping` handler** (`route "/ping" |> text "pong"`)
with **no per-request logging**, and [oha](https://github.com/hatoo/oha) drives the
same load at each. The apps are deliberately minimal so the numbers reflect the
framework, not application code.

## Running

```console
just bench             # all targets: dotnet python js beam
just bench python js   # a subset
```

Or call the script directly:

```console
perf/bench.sh                          # all targets
REQUESTS=20000 CONNECTIONS=200 perf/bench.sh dotnet beam
```

Requires [oha](https://github.com/hatoo/oha) on `PATH`, plus the per-target
toolchains already listed in the top-level README (uv, Node, Erlang/rebar3, .NET).

## Layout

| Path | Target | How it's served |
|---|---|---|
| `dotnet/` | Real Giraffe on ASP.NET Core (reference) | Kestrel, `dotnet` (port 8083) |
| `python/` | `Fable.Giraffe.Python` | uvicorn, 1 worker (port 8081) |
| `js/`     | `Fable.Giraffe.Js`     | `node:http` (port 8082) |
| `beam/`   | `Fable.Giraffe.Beam`   | Cowboy (port 8084) |

`bench.sh` builds each target, boots it in its own process group, waits for
`/ping` to answer, warms up, runs oha, then tears the server down.

## Why "no logging" matters

The Python and JS middleware emit a per-request access log **only when
`LogLevel.Debug` is enabled** (see `src/python/Middleware.fs`). The example apps
in `app/` turn that on deliberately; the BEAM app configures no logger at all. A
benchmark that left those settings as-is would compare a logging server against a
non-logging one. The perf apps here all pin logging off (`Warning` minimum level
on the Fable targets, `ClearProviders()` on .NET) so the comparison is like-for-like.

Standard framework-throughput practice (e.g. TechEmpower) is to benchmark without
per-request logging, which is what this harness does.
