module Program

open Fable.Giraffe
open Fable.Giraffe.Pipelines
open Fable.Logging

type Model = { Name: string; Age: int }

let webApp =
    choose [
        route "/ping" |> HttpHandler.text "pong"
        route "/json" |> HttpHandler.json { Name = "Dag"; Age = 53 }
    ]

// No per-request logging: a Warning minimum level keeps the middleware's
// Debug-gated access log (src/python/Middleware.fs) from ever firing. This is
// the fair baseline — the example app in app/ deliberately runs at Debug.
let app =
    WebHostBuilder()
        .ConfigureLogging(fun builder -> builder.SetMinimumLevel(LogLevel.Warning))
        .Configure(fun app -> app.UseGiraffe(webApp))
        .Build()
