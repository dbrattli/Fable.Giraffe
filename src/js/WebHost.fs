namespace Fable.Giraffe

open System

open Fable.Core
open Fable.Logging

/// Application builder — collects the Giraffe handler and services.
type IApplicationBuilder =
    abstract ApplicationServices: ServiceCollection with get, set
    abstract UseGiraffe: HttpHandler -> unit

/// Host builder for the JS/Node backend.
///
/// Unlike the Python backend (which returns an ASGI app for uvicorn to serve),
/// and like the BEAM backend (which starts Cowboy directly), the JS builder
/// hosts the app itself — either as a zero-dependency `http` server via `Run`,
/// or as a Connect/Express middleware via `ToMiddleware` for ecosystem reuse.
type WebHostBuilder() =
    let loggerFactory = LoggerFactory.Create()
    let services = ServiceCollection()
    let mutable handler: HttpHandler option = None

    member private _.Handler =
        match handler with
        | Some h -> h
        | None -> failwith "No handler configured. Call UseGiraffe in Configure."

    member this.Configure(configureApp: Action<IApplicationBuilder>) =
        let app =
            { new IApplicationBuilder with
                member _.ApplicationServices
                    with get () = services
                    and set _ = ()

                member _.UseGiraffe(h: HttpHandler) = handler <- Some h }

        configureApp.Invoke(app)
        this

    member this.ConfigureLogging(configureLogging: Action<ILoggingBuilder>) =
        let loggingBuilder = loggerFactory :> ILoggingBuilder
        configureLogging.Invoke(loggingBuilder)

        let logger = loggerFactory.CreateLogger("Giraffe")
        services.AddSingleton(logger)
        this

    /// Log to the console via Fable.Logging's JS (console) provider — the JS
    /// counterpart of the Python backend's `UseStructlog`.
    member this.UseConsoleLogging() =
        this.ConfigureLogging(fun builder -> builder.AddProvider(new Fable.Logging.JS.LoggerProvider()))

    /// Expose the app as a Connect/Express middleware for `app.use(...)`.
    member this.ToMiddleware() =
        Server.toMiddleware this.Handler services

    /// Start a zero-dependency Node `http` server on `port`.
    member this.Run(port: int) = Server.run this.Handler services port

module Host =
    let CreateDefaultBuilder (_: string array) = WebHostBuilder()
