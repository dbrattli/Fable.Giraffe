namespace Fable.Giraffe

open System
open System.Threading.Tasks

open Fable.Logging

type ASGIApp = Func<Scope, unit -> Task<Response>, Request -> Task<unit>, Task<unit>>

type IApplicationBuilder =
    abstract ApplicationServices: ServiceCollection with get, set

    abstract UseMiddleware: Func<ILoggerFactory, obj> -> IApplicationBuilder

type IMiddleware =
    abstract Invoke: HttpContext -> Task<unit>

type IWebHostBuilder =
    abstract Configure: Action<IApplicationBuilder> -> IWebHostBuilder
    abstract ConfigureLogging: Action<ILoggingBuilder> -> IWebHostBuilder
    abstract Build: unit -> ASGIApp

type WebHostBuilder() =
    let loggerFactory = LoggerFactory.Create()
    let services = ServiceCollection()
    let pipelines = ResizeArray<IMiddleware>()

    //logger.LogInformation("Giraffe ASGI middleware initialized")

    let asgiApp =
        Func<Scope, unit -> Task<Response>, Request -> Task<unit>, Task<unit>>(fun scope receive send ->
            task {
                let ctx = HttpContext(scope, receive, send, services)

                return! pipelines.[0].Invoke(ctx)
            })

    interface IWebHostBuilder with
        member this.Configure(configureApp: Action<IApplicationBuilder>) =
            let app =
                { new IApplicationBuilder with
                    member this.UseMiddleware(func: Func<ILoggerFactory, obj>) =
                        let pipeline = (func.Invoke(loggerFactory) :?> IMiddleware)

                        pipelines.Add(pipeline)
                        this

                    member this.ApplicationServices
                        with get () = services
                        and set (_) = ()
                }

            configureApp.Invoke(app)
            this

        member this.ConfigureLogging(configureLogging: Action<ILoggingBuilder>) =
            let loggingBuilder = loggerFactory :> ILoggingBuilder

            configureLogging.Invoke(loggingBuilder)

            let logger = loggerFactory.CreateLogger("Giraffe")
            services.AddSingleton(logger)

            this

        //member this.WithLoggerFactory (loggerFactory: ILoggerFactory) = { webHost with LoggerFactory = loggerFactory }
        member this.Build() = asgiApp

    member this.ConfigureLogging(configureLogging: Action<ILoggingBuilder>) =
        (this :> IWebHostBuilder).ConfigureLogging(configureLogging)


module Host =
    let CreateDefaultBuilder (args: string array) =
        let builder = WebHostBuilder()
        builder

[<AutoOpen>]
module Extensions =
    type IWebHostBuilder with

        member this.UseStructlog() =
            this.ConfigureLogging(fun builder -> builder.AddProvider(new Structlog.ConsoleLoggerProvider()))
            |> ignore

            this
