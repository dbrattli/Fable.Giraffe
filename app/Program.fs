module Program

open System

open Fable.Giraffe
open Fable.Giraffe.Pipelines

open Fable.Logging

type Model = { Name: string; Age: int }

let method (source: HttpHandler) =
    fun next (ctx: HttpContext) -> task {
        let log = ctx.GetService<ILogger>()
        log.LogDebug("Hello from Fable.Giraffe!")

        return! next ctx
    }
    |> subscribe source

let webApp =
    choose [
        route "/ping" |> HttpHandler.text "pong"

        route "/json"
        |> HttpHandler.json { Name = "Dag"; Age = 53 }

        route "/log"
        |> method
        |> HttpHandler.text "logged"
    ]

let provider = new Structlog.ConsoleLoggerProvider()

let app =
    WebHostBuilder()
        .ConfigureLogging(fun builder -> builder.SetMinimumLevel(LogLevel.Debug))
        .UseStructlog()
        .Configure(Action<IApplicationBuilder>(fun app -> app.UseGiraffe(webApp)))
        .Build()
