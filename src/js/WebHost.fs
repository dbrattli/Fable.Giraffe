namespace Fable.Giraffe

open System

open Fable.Core
open Fable.Logging

/// Application builder — collects the Giraffe handler and services.
type IApplicationBuilder =
    abstract ApplicationServices: ServiceCollection with get, set
    abstract UseGiraffe: HttpHandler -> unit

    /// Serve files from <c>directory</c> under the URL prefix <c>requestPath</c> (e.g.
    /// <c>UseStaticFiles("/static", "public")</c> maps <c>GET /static/app.css</c> to
    /// <c>public/app.css</c>). An empty <c>requestPath</c> mounts at the root. Requests that
    /// don't resolve to a file fall through to Giraffe. Backed by serve-static.
    abstract UseStaticFiles: string * string -> unit

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
    let staticMounts = ResizeArray<string * string>()

    member private _.Handler =
        match handler with
        | Some h -> h
        | None -> failwith "No handler configured. Call UseGiraffe in Configure."

    member private _.StaticMounts = List.ofSeq staticMounts

    member this.Configure(configureApp: Action<IApplicationBuilder>) =
        let app =
            { new IApplicationBuilder with
                member _.ApplicationServices
                    with get () = services
                    and set _ = ()

                member _.UseGiraffe(h: HttpHandler) = handler <- Some h

                member _.UseStaticFiles(requestPath: string, directory: string) =
                    staticMounts.Add(requestPath, directory) }

        configureApp.Invoke(app)
        this

    member this.ConfigureLogging(configureLogging: Action<ILoggingBuilder>) =
        Logging.configure loggerFactory services configureLogging
        this

    /// Log to the console via Fable.Logging's JS (console) provider — the JS
    /// counterpart of the Python backend's `UseStructlog`.
    member this.UseConsoleLogging() =
        this.ConfigureLogging(fun builder -> builder.AddProvider(new Fable.Logging.JS.LoggerProvider()))

    /// Expose the app as a Connect/Express middleware for `app.use(...)`.
    member this.ToMiddleware() =
        Server.toMiddleware this.StaticMounts this.Handler services

    /// Start a Node `http` server on `port`.
    member this.Run(port: int) =
        Server.run this.StaticMounts this.Handler services port

module Host =
    let CreateDefaultBuilder (_: string array) = WebHostBuilder()

[<AutoOpen>]
module Extensions =
    type IApplicationBuilder with

        /// Serve files from <c>directory</c> at the URL root, falling through to Giraffe on a
        /// miss. Shorthand for <c>UseStaticFiles("", directory)</c>.
        member x.UseStaticFiles(directory: string) = x.UseStaticFiles("", directory)
