namespace Fable.Giraffe

open System
open System.Threading.Tasks
open System.Collections.Generic

open Fable.Core
open Fable.Logging

type RequestDelegate = HttpContext -> Task<unit>

/// https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.imiddleware?view=aspnetcore-6.0
type IMiddleware =
    [<Emit("$0($1, $2, $3)")>]
    abstract __call__: scope: Scope * receive: (unit -> Task<Response>) * send: (Request -> Task<unit>) -> Task<unit>

type Middleware (cls: Type, options: IDictionary<string, obj>) =
    do ()


type IApplicationBuilder =
    abstract ApplicationServices: ServiceCollection with get, set

    abstract UseMiddleware: Func<ILoggerFactory, IMiddleware> -> IApplicationBuilder

/// https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.hosting.iwebhostbuilder?view=aspnetcore-6.0
type IWebHostBuilder =
    abstract Configure: Action<IApplicationBuilder> -> IWebHostBuilder
    abstract ConfigureLogging: Action<ILoggingBuilder> -> IWebHostBuilder
    abstract Build: unit -> ASGIApp

type WebHostBuilder() =
    let loggerFactory = LoggerFactory.Create()
    let services = ServiceCollection()
    let pipelines = ResizeArray<IMiddleware>()

    let asgiApp =
        Func<Scope, unit -> Task<Response>, Request -> Task<unit>, Task<unit>>(fun scope receive send ->
            task {
                scope["services"] <- services
                let ctx = HttpContext(scope, receive, send)

                let next ctx = task {
                    return ()
                }
                for pipeline in pipelines do
                    do! pipeline.InvokeAsync(ctx, next)

            })

    interface IWebHostBuilder with
        member this.Configure(configureApp: Action<IApplicationBuilder>) =
            let app =
                { new IApplicationBuilder with
                    member this.UseMiddleware(func: Func<ILoggerFactory, IMiddleware>) =
                        let pipeline = func.Invoke(loggerFactory)

                        pipelines.Add(pipeline)
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

        member x.UseMiddleware(app: ASGIApp) : IApplicationBuilder =
            let middleware = { new IMiddleware with
                member x.InvokeAsync(ctx: HttpContext, next: RequestDelegate) = task {
                    return! ctx.ContinueWith(app, next)
                }
            }
            x.UseMiddleware(fun loggerFactory -> middleware)
