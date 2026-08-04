# Fable.Giraffe

[![Build and Test](https://github.com/dbrattli/Fable.Giraffe/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/dbrattli/Fable.Giraffe/actions/workflows/build-and-test.yml)
[![Fable.Giraffe.Python](https://img.shields.io/nuget/v/Fable.Giraffe.Python?label=Fable.Giraffe.Python)](https://www.nuget.org/packages/Fable.Giraffe.Python/)
[![Fable.Giraffe.Js](https://img.shields.io/nuget/v/Fable.Giraffe.Js?label=Fable.Giraffe.Js)](https://www.nuget.org/packages/Fable.Giraffe.Js/)
[![Fable.Giraffe.Beam](https://img.shields.io/nuget/v/Fable.Giraffe.Beam?label=Fable.Giraffe.Beam)](https://www.nuget.org/packages/Fable.Giraffe.Beam/)

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
line is built with and requires Fable 5. The major is therefore *not* a SemVer
signal — a breaking change can land in a minor release, and will be listed under
**Breaking changes** in [CHANGELOG.md](CHANGELOG.md). Read it before upgrading.

Beyond the Giraffe handler API, it ships an opt-in
[endpoint layer](#openapi) that generates an **OpenAPI 3.1** document and serves
interactive docs, [typed JSON](#json-and-validation) with FastAPI-style
validation errors, and [remoting](#remoting) — all shared across the three
targets.

## Install

There is one NuGet package per target — add the one for the runtime you are
compiling to (all three share the same handler API):

```console
dotnet add package Fable.Giraffe.Python   # Python / ASGI
dotnet add package Fable.Giraffe.Js       # JavaScript / Node.js
dotnet add package Fable.Giraffe.Beam     # Erlang / BEAM
```

You also need the Fable compiler (`dotnet tool install fable`) and the target's
runtime dependencies — see [Prerequisites](#prerequisites).

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

## JSON and validation

JSON goes through [Fable.TypedJson](https://github.com/dbrattli/Fable.TypedJson),
which derives a type's decoder, encoder **and JSON Schema from a single walk**.
That single walk is the point: the schema a generated OpenAPI document publishes
cannot disagree with what the serializer actually writes. In the ASP.NET world
these come from two separate mechanisms — Swashbuckle and `System.Text.Json` —
and can drift.

Records serialize to **camelCase** on every target, and unions to a tagged
`{"type": "caseName", ...}`:

```fsharp
type Order = { Customer: string; LineTotal: decimal }

json { Customer = "Ada"; LineTotal = 12.34m }
// {"customer":"Ada","lineTotal":"12.34"}
```

Field *names* are identical across targets; exact bytes are not, and cannot be.
Python's `json.dumps` adds `", "` spacing, and Erlang maps have no insertion
order, so BEAM emits keys in term order. Compare parsed JSON, not strings.

`DateTime` crosses the wire as ISO-8601 UTC, `Guid` as a canonical uuid, and
`decimal` as a **string** — a decimal exists precisely because binary floating
point cannot represent the value, so emitting it as a JSON number would throw
away the guarantee the type was chosen for. Pydantic does the same.

### Validation

`validateJson<'T>` answers **422** with a per-field error list instead of
throwing, the way FastAPI does:

```fsharp
POST [ route "/greet" (validateJson<Model> (fun m -> text $"Hello, {m.Name}")) ]
```

```console
$ curl -X POST localhost:8080/greet -d '{"name":"Ada","age":"nope"}'
{"detail":[{"loc":"age","msg":"cannot parse 'nope' as int"}]}
```

Nested failures report a path (`address.city`, `members[1].city`). `bindJson`
still throws — `validateJson` is additive, so you opt in per route.

## OpenAPI

Upstream Giraffe generates OpenAPI through
[Giraffe.OpenApi](https://github.com/giraffe-fsharp/Giraffe.OpenApi), which
requires `Giraffe.EndpointRouting` and ASP.NET's document pipeline. Neither is
available under Fable, and the reason is structural: `HttpHandler` is a bare
closure, so `route "/ping"` erases `"/ping"` and a composed application carries
no description of itself.

Fable.Giraffe solves it the way Giraffe and FastAPI both do — a declaration-time
route table. `Fable.Giraffe.Endpoints` is an **opt-in** layer that lowers onto
the ordinary `route` / `routef` / `subRoute` / `choose` combinators, so there is
still exactly one path matcher and classic `choose [ ... ]` apps are unaffected:

```fsharp
open Fable.Giraffe
open Fable.Giraffe.Endpoints

type User = { Name: string; Age: int }

let endpoints = [
    GET [
        route "/ping" (text "pong")
        |> summary "Health check"
        |> respondsWith<string> 200 "text/plain"

        routef "/user/%i" getUser
        |> pathParams [ "id" ]
        |> responds<User> 200
        |> respondsEmpty 404
    ]

    POST [
        route "/user" createUser
        |> accepts<User>
        |> responds<User> 201
    ]

    // Metadata set on a group is inherited by its leaves.
    subRoute "/admin" [ GET [ route "/stats" getStats |> responds<Stats> 200 ] ]
    |> tags [ "admin" ]
]

// The annotation matters: without something consuming `webApp`, F#'s value
// restriction rejects the binding.
let webApp: HttpHandler =
    endpoints
    |> OpenApi.withDocs (OpenApiInfo.Create("My API", "1.0"))
    |> Endpoints.toHandler
```

That serves the document at `/openapi.json` and an interactive
[Scalar](https://github.com/scalar/scalar) UI at `/docs`. Path templates and
their parameter types are derived from the `routef` format string; response and
request schemas come from `typeof<'T>` captured at the call site. Types are
emitted once into `components/schemas` and referenced by `$ref`, so a shared type
is defined once and a recursive one round-trips.

The document is built **once, at startup**, and the handlers close over the
rendered string — which is also what makes it work on BEAM, where Cowboy spawns
a fresh process per request and only immutable values survive the hop.

Three things worth knowing:

- A route that answers **any** verb (one not inside a `GET [...]`-style group)
  still routes, but is absent from the document — OpenAPI has no "any method"
  operation.
- `routef` templates carry no parameter names, so without `pathParams` they are
  named positionally (`p0`, `p1`).
- `responds<'T>` documents `application/json`. A handler writing `text/plain`
  should use `respondsWith<'T> 200 "text/plain"`, or the document will misdescribe
  it.

## Remoting

`Remoting` reflects over a record of `... -> Async<'T>` fields and generates one
route per field, decoding arguments and encoding results with the same typed JSON
machinery:

```fsharp
type IServer =
    { getNumbers: unit -> Async<int list>
      updateModel: Model -> Async<Model> }

let webApp =
    Remoting.createApi ()
    |> Remoting.fromValue server
    |> Remoting.buildHttpHandler
```

A malformed body answers 400 with field-level errors; an exception raised by an
API method answers 500 without leaking it (`Remoting.withErrorHandler` replaces
that default).

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
| Erlang/BEAM (Cowboy) | ~224,000 | 0.42 ms | 1.71 ms |
| JavaScript (Node) | ~63,000 | 1.56 ms | 3.24 ms |
| Python (uvicorn, 1 worker) | ~14,600 | 6.79 ms | 11.90 ms |

These numbers are machine-dependent and only meaningful relative to one another;
throughput at the top of the table is noisy because the framework outruns the
loopback/oha harness driving it. The harness lives in [`perf/`](perf/) — reproduce
with `just bench` (or `just bench python js` for a subset).

Every target must share the same logging configuration for the comparison to
mean anything: an earlier version of this table ran with logging enabled on .NET
but not BEAM, which made .NET look slower than BEAM. The current run turns
per-request logging off everywhere.
