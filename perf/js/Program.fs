module Program

open Fable.Giraffe
open Fable.Giraffe.Pipelines
open Fable.Logging

type Model = { Name: string; Age: int }

let webApp =
    choose
        [ route "/ping" |> HttpHandler.text "pong"
          route "/json" |> HttpHandler.json { Name = "Dag"; Age = 53 } ]

// No per-request logging: a Warning minimum level keeps the Debug-gated access
// log from firing. The example app in app/js deliberately runs at Debug.
WebHostBuilder()
    .ConfigureLogging(fun builder -> builder.SetMinimumLevel(LogLevel.Warning))
    .Configure(fun app -> app.UseGiraffe(webApp))
    .Run(8082)
