namespace Fable.Giraffe

open System
open System.Threading.Tasks

open Fable.Giraffe.Pipelines
open Fable.Logging

type App = Func<Scope, unit -> Task<Response>, Request -> Task<unit>, Task<unit>>

module GiraffeMiddleware =
    let loggerFactory = new LoggerFactory()

    let useLogger (provider: ILoggerProvider) = loggerFactory.AddProvider(provider)

    let useGiraffe (handler: HttpHandler) : App =
        let defaultHandler =
            setStatusCode 404
            |> HttpHandler.text "Not found!"

        // pre-compile the handler pipeline
        let func: HttpFunc =
            choose [ handler; defaultHandler ] earlyReturn

        let defaultFunc: HttpFunc =
            defaultHandler earlyReturn

        let services = ServiceCollection()

        // Setup logging
        let logger =
            loggerFactory.CreateLogger("Giraffe")

        services.AddSingleton(logger)

        logger.LogInformation("Giraffe ASGI middleware initialized")

        let app (scope: Scope) (receive: unit -> Task<Response>) (send: Request -> Task<unit>) =
            task {
                let ctx =
                    HttpContext(scope, receive, send, services)

                if ctx.Request.Protocol = "http" then
                    let start =
                        Diagnostics.Stopwatch.GetTimestamp()

                    let! result = func ctx

                    if logger.IsEnabled LogLevel.Debug then
                        let freq =
                            double System.Diagnostics.Stopwatch.Frequency

                        let stop =
                            Diagnostics.Stopwatch.GetTimestamp()

                        let elapsedMs =
                            (double (stop - start)) * 1000.0 / freq

                        let logLevel =
                            match ctx.Response.StatusCode with
                            | code when code < 300 -> LogLevel.Information
                            | code when code < 500 -> LogLevel.Error
                            | _ -> LogLevel.Critical

                        logger.Log(
                            logLevel,
                            "Giraffe returned {Status} for {HttpProtocol} {HttpMethod} at {Path} in {ElapsedMs}",
                            parameters =
                                [|
                                    ctx.Response.StatusCode
                                    ctx.Request.Protocol
                                    ctx.Request.Method
                                    ctx.Request.Path.ToString()
                                    elapsedMs
                                |]
                        )

                return ()
            }

        // Return a tupled function so it may be used from Python
        Func<_, _, _, _>(app)
