namespace Fable.Giraffe

open System
open System.Threading.Tasks
open Fable.Giraffe.Pipelines
open Fable.Logging

type IApplicationBuilder =
    abstract ApplicationServices: ServiceCollection with get, set

    abstract UseMiddleware: Func<ASGIApp, ILoggerFactory, ASGIApp> -> IApplicationBuilder

/// https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.hosting.iwebhostbuilder?view=aspnetcore-6.0
type IWebHostBuilder =
    abstract Configure: Action<IApplicationBuilder> -> IWebHostBuilder
    abstract ConfigureLogging: Action<ILoggingBuilder> -> IWebHostBuilder
    abstract Build: unit -> ASGIApp

type WebHostBuilder() =
    let loggerFactory = LoggerFactory.Create()
    let services = ServiceCollection()
    let pipelines = ResizeArray<Func<ASGIApp, ILoggerFactory, ASGIApp>>()

    let notFound =
        (setStatusCode 404
         |> HttpHandler.text "Not Found")
            earlyReturn

    let defaultApp =
        fun (scope: Scope) (receive: unit -> Task<Response>) (send: Request -> Task<unit>) -> task {
            let! _ =
                HttpContext(scope, receive, send)
                |> notFound

            return ()
        }

    interface IWebHostBuilder with
        member this.Configure(configureApp: Action<IApplicationBuilder>) =
            let app =
                { new IApplicationBuilder with
                    member this.UseMiddleware(func: Func<ASGIApp, ILoggerFactory, ASGIApp>) =
                        pipelines.Add(func)
                        this

                    member this.ApplicationServices
                        with get () = services
                        and set _ = ()
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
        member this.Build() : ASGIApp =
            let mutable app: ASGIApp = defaultApp

            for pipeline in pipelines |> Seq.rev do
                app <- pipeline.Invoke(app, loggerFactory)

            Func<Scope, unit -> Task<Response>, Request -> Task<unit>, Task<unit>>(fun scope receive send -> task {
                scope["services"] <- services

                return! app.Invoke(scope, receive, send)
            })

    member this.ConfigureLogging(configureLogging: Action<ILoggingBuilder>) =
        (this :> IWebHostBuilder).ConfigureLogging(configureLogging)


module Host =
    let CreateDefaultBuilder (_: string array) =
        let builder = WebHostBuilder()
        builder

[<AutoOpen>]
module Extensions =
    type IWebHostBuilder with

        member this.UseStructlog(?json: bool) =
            let provider =
                if json.IsSome then
                    new Structlog.JsonLoggerProvider() :> ILoggerProvider
                else
                    new Structlog.ConsoleLoggerProvider()

            this.ConfigureLogging(fun builder -> builder.AddProvider(provider))
            |> ignore

            this

    type IApplicationBuilder with

        member x.UseMiddleware(app: ASGIApp -> ASGIApp) : IApplicationBuilder =
            x.UseMiddleware(fun next loggerFactory -> app next)
