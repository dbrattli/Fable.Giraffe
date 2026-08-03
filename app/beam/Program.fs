module Program

open Fable.Giraffe
open Fable.Giraffe.Pipelines
open Fable.Logging

type Model = { Name: string; Age: int }

let webApp =
    choose
        [ route "/ping" |> HttpHandler.text "pong"

          route "/json"
          |> HttpHandler.json { Name = "Dag"; Age = 53 } ]

let start () =
    WebHostBuilder()
        .ConfigureLogging(fun builder -> builder.SetMinimumLevel(LogLevel.Debug))
        .UseBeamLogging()
        .Configure(fun app ->
            app.UseStaticFiles("/static", "app/public")
            app.UseGiraffe(webApp))
        .Build(8080)
