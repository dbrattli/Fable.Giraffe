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
        // pre-compile the handler pipeline
        let func: HttpFunc = handler earlyReturn

        let defaultHandler =
            setStatusCode 404
            |> HttpHandler.text "Not found!"

        let defaultFunc: HttpFunc = defaultHandler earlyReturn

        let services = ServiceCollection()

        // Setup logging
        let logger = loggerFactory.CreateLogger("Giraffe")
        services.AddSingleton(logger)

        logger.LogInformation("Giraffe ASGI middleware initialized")

        let app (scope: Scope) (receive: unit -> Task<Response>) (send: Request -> Task<unit>) = task {
            let ctx = HttpContext(scope, receive, send, services)

            if ctx.Request.Protocol = "http" then
                let start = Diagnostics.Stopwatch.GetTimestamp()

                let! result = func ctx

                if logger.IsEnabled LogLevel.Debug then
                    let freq = double System.Diagnostics.Stopwatch.Frequency
                    let stop = Diagnostics.Stopwatch.GetTimestamp()
                    let elapsedMs = (double (stop - start)) * 1000.0 / freq

                    logger.LogDebug(
                        "Giraffe returned {SomeNoneResult} for {HttpProtocol} {HttpMethod} at {Path} in {ElapsedMs}",
                        (if result.IsSome then "Some" else "None"),
                        ctx.Request.Protocol,
                        ctx.Request.Method,
                        ctx.Request.Path.ToString(),
                        elapsedMs
                    )

                if result.IsNone then
                    let! _ = defaultFunc ctx
                    return ()
                else
                    return ()
            else
                return ()
        }

        // Return a tupled function so it may be used from Python
        Func<_, _, _, _>(app)
