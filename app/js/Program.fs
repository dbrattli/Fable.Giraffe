module Program

open Fable.Giraffe
open Fable.Giraffe.Pipelines

open Fable.Logging

type Model = { Name: string; Age: int }

let loggingHandler (source: HttpHandler) =
    fun next (ctx: HttpContext) ->
        task {
            let log = ctx.GetService<ILogger>()
            log.LogDebug("Hello from Fable.Giraffe on Node!")

            return! text "Logged" next ctx
        }
    |> subscribe source

let echo: HttpHandler =
    fun next (ctx: HttpContext) ->
        task {
            let! body = ctx.ReadBodyFromRequestAsync()
            return! text body next ctx
        }

let webApp =
    choose
        [ route "/ping" |> HttpHandler.text "pong"

          route "/json"
          |> HttpHandler.json { Name = "Dag"; Age = 53 }

          route "/echo" >=> echo

          route "/log" |> loggingHandler ]

WebHostBuilder()
    .ConfigureLogging(fun builder -> builder.SetMinimumLevel(LogLevel.Debug))
    .UseConsoleLogging()
    .Configure(fun app ->
        app.UseStaticFiles("/static", "app/public")
        app.UseGiraffe(webApp))
    .Run(8080)
