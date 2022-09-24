namespace Fable.Giraffe

open System
open System.Threading.Tasks

open Fable.Logging

type GiraffeMiddleware(app: ASGIApp, handler: HttpHandler, loggerFactory: ILoggerFactory) =
    let logger = loggerFactory.CreateLogger("GiraffeMiddleware")
    let freq = double Diagnostics.Stopwatch.Frequency

    // pre-compile the handler pipeline
    let func: HttpFunc = handler earlyReturn

    member x.InvokeAsync (scope: Scope) (receive: unit -> Task<Response>) (send: Request -> Task<unit>) = task {
        let ctx = HttpContext(scope, receive, send)

        if ctx.Request.Protocol = "http" then
            let start = Diagnostics.Stopwatch.GetTimestamp()

            let! result = func ctx

            if logger.IsEnabled LogLevel.Debug then
                let stop = Diagnostics.Stopwatch.GetTimestamp()

                let elapsedMs = (double (stop - start)) * 1000.0 / freq

                let logLevel =
                    match ctx.Response.StatusCode with
                    | code when code < 300 -> LogLevel.Information
                    | code when code < 500 -> LogLevel.Error
                    | _ -> LogLevel.Critical

                logger.Log(
                    logLevel,
                    "Giraffe returned {Status} for {HttpProtocol} {HttpMethod} at {Path} in {ElapsedMs}",
                    parameters = [|
                        ctx.Response.StatusCode :> obj
                        ctx.Request.Protocol
                        ctx.Request.Method
                        ctx.Request.Path.ToString()
                        elapsedMs
                    |]
                )

            if result.IsNone then
                return! app.Invoke(scope, receive, send)
    }

[<AutoOpen>]
module GiraffeMiddleware =
    type IApplicationBuilder with

        member x.UseGiraffe(handler: HttpHandler) : unit =
            x.UseMiddleware(fun app loggerFactory ->
                let middleware = GiraffeMiddleware(app, handler, loggerFactory)
                Func<Scope, unit -> Task<Response>, Request -> Task<unit>, Task<unit>>(middleware.InvokeAsync))
            |> ignore
