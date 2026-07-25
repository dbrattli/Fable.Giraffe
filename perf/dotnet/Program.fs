module Program

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Giraffe

// The real Giraffe running on ASP.NET Core / Kestrel — the reference point the
// Fable targets are measured against. The handler pipeline mirrors the Fable
// perf apps exactly.
let webApp =
    choose [
        route "/ping" >=> text "pong"
        route "/json" >=> json {| Name = "Dag"; Age = 53 |}
    ]

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    builder.Services.AddGiraffe() |> ignore

    // Fair baseline: no per-request logging, matching the Fable targets (whose
    // access log is Debug-gated and left off here). Clearing providers also drops
    // Kestrel/hosting startup chatter.
    builder.Logging.ClearProviders().SetMinimumLevel(LogLevel.Warning) |> ignore

    let app = builder.Build()
    app.UseGiraffe(webApp)
    app.Run("http://127.0.0.1:8083")
    0
