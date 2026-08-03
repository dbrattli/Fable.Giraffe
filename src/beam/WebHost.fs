namespace Fable.Giraffe

open System
open Fable.Core
open Fable.Beam
open Fable.Beam.Cowboy
open Fable.Logging

module Cowboy = Fable.Beam.Cowboy.Cowboy
module CowboyRouter = Fable.Beam.Cowboy.CowboyRouter

module CowboyFFI =
    /// Atom for the listener name.
    [<Emit("http")>]
    let httpAtom: Atom = nativeOnly

    /// The Erlang module atom implementing the cowboy_handler behaviour.
    /// Fable >= 5.8 qualifies generated BEAM module names with the source path inside the
    /// assembly, so src/beam/Middleware.fs compiles to `src_beam_middleware` rather than
    /// `middleware`. This atom is hand-written, so it has to track that name: move or rename
    /// Middleware.fs and this must change with it, or Cowboy fails with `undef` on init/2.
    [<Emit("src_beam_middleware")>]
    let middlewareAtom: Atom = nativeOnly

    /// Cowboy's built-in static-file handler module. We delegate the whole static-serving
    /// problem — mime detection, ETags, Range requests, conditional requests — to it rather
    /// than reimplement any of that.
    [<Emit("cowboy_static")>]
    let cowboyStaticAtom: Atom = nativeOnly

    /// cowboy_static init state serving a directory: `{dir, Dir, [{mimetypes, cow_mimetypes, all}]}`.
    /// `Dir` is a binary here (an F# string on BEAM); cowboy_static's `absname/1` accepts binaries.
    /// The mimetypes extra routes content-type guessing through cowlib's cow_mimetypes:all/1.
    [<Emit("{dir, $0, [{mimetypes, cow_mimetypes, all}]}")>]
    let dirState (directory: string) : obj = nativeOnly

type IApplicationBuilder =
    abstract ApplicationServices: ServiceCollection with get, set
    abstract UseGiraffe: HttpHandler -> unit

    /// Serve files from <c>directory</c> under the URL prefix <c>requestPath</c> (e.g.
    /// <c>UseStaticFiles("/static", "public")</c> maps <c>GET /static/app.css</c> to
    /// <c>public/app.css</c>). Requests that don't match the prefix fall through to Giraffe.
    ///
    /// Unlike the Python/JS backends there is no root-mount (empty-prefix) form: Cowboy routing
    /// is exclusive longest-prefix matching with no fall-through, so a root static mount would
    /// shadow the Giraffe catch-all entirely. A prefix is therefore required.
    abstract UseStaticFiles: string * string -> unit

/// A process-portable ILogger for BEAM.
///
/// Fable compiles a class with ANY mutable field to a process-dictionary ref, and a class
/// without one to a plain map. Every Fable.Logging logger is in the first category
/// (`Fable.Logging.Beam.Logger` has a settable `MinimumLevel`; the factory's own `Logger`
/// mutates its provider list), so a logger built here is unreadable inside the fresh process
/// Cowboy spawns per request — `erlang:get(Ref)` there returns `undefined`.
///
/// An object expression, by contrast, compiles to a self-contained map of funs, which is an
/// ordinary term and crosses the boundary intact. This one closes over nothing but a string
/// and a LogLevel, and writes to OTP's `logger` — a global service reachable from any process,
/// and exactly where Fable.Logging.Beam's provider sends its own output.
module PortableLogger =

    /// `LogLevel.None` (6) sorts above `Critical` (5), so it doubles as the "disabled"
    /// sentinel: every real level compares below it and nothing is emitted.
    let create (categoryName: string) (minimumLevel: LogLevel) : ILogger =
        { new ILogger with
            member _.IsEnabled(logLevel: LogLevel) = logLevel >= minimumLevel

            member _.Log(state: LogState) =
                if state.Level >= minimumLevel then
                    // translateFormat is Fable.Logging's own placeholder renderer, written with
                    // plain string ops precisely so it runs on BEAM.
                    let message, _ = Common.translateFormat categoryName state.Format state.Args

                    match state.Level with
                    | LogLevel.Trace
                    | LogLevel.Debug -> Logger.logger.debug message
                    | LogLevel.Information -> Logger.logger.info message
                    | LogLevel.Warning -> Logger.logger.warning message
                    | LogLevel.Error -> Logger.logger.error message
                    | LogLevel.Critical -> Logger.logger.critical message
                    | _ -> Logger.logger.info message

            member _.BeginScope(_: obj) : IDisposable = failwith "Not implemented" }

    /// Read the factory's effective threshold back out as a plain value.
    ///
    /// LoggerFactory exposes no minimum-level getter, but `IsEnabled` answers the same
    /// question, so probe it from the bottom up and keep the lowest level that passes.
    /// `IsEnabled` is also false for every level when no provider is registered, which is
    /// what makes an unconfigured host produce a silent logger rather than noise.
    let effectiveLevel (probe: ILogger) =
        [ LogLevel.Trace
          LogLevel.Debug
          LogLevel.Information
          LogLevel.Warning
          LogLevel.Error
          LogLevel.Critical ]
        |> List.tryFind probe.IsEnabled
        |> Option.defaultValue LogLevel.None

type IWebHostBuilder =
    abstract Configure: Action<IApplicationBuilder> -> IWebHostBuilder
    abstract Build: int -> unit

type WebHostBuilder() =
    let loggerFactory = LoggerFactory.Create()
    let services = ServiceCollection()
    let mutable handler: HttpHandler option = None
    let staticMounts = ResizeArray<string * string>()

    // Whether ConfigureLogging ran, so Build registers an ILogger only when the other backends
    // would have one. Without this an unconfigured BEAM host would hand out a silent no-op
    // logger where Python/JS raise "Service not found" — a divergence for no benefit.
    let mutable loggingConfigured = false

    interface IWebHostBuilder with
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

        member this.Build(port: int) =
            match handler with
            | None -> failwith "No handler configured. Call UseGiraffe in Configure."
            | Some h ->
                // Build Cowboy routing dispatch. Static mounts are compiled to cowboy_static
                // routes at their URL prefix and placed AHEAD of the Giraffe catch-all, since
                // Cowboy matches the most specific route and never falls through.
                let staticRoutes =
                    staticMounts
                    |> Seq.map (fun (requestPath, directory) ->
                        CowboyRouter.route (requestPath + "/[...]") CowboyFFI.cowboyStaticAtom (CowboyFFI.dirState directory))
                    |> List.ofSeq

                // Compose the handler pipeline ONCE here rather than per request. Cowboy spawns
                // a fresh handler process per request, so anything left in init/2 runs on the hot
                // path; `h earlyReturn` is pure and reusable (the Python backend likewise composes
                // once in its middleware constructor).
                let func: HttpFunc = h earlyReturn

                // Resolve the configured log level to a plain value once, here. Build is the
                // right place because it is terminal — it starts the listener, so no further
                // ConfigureLogging call can change the level afterwards.
                let level = PortableLogger.effectiveLevel (loggerFactory.CreateLogger("Giraffe"))

                // Replace the "Giraffe" logger that Logging.configure registered. That one comes
                // from LoggerFactory and is therefore ref-backed, so resolving it via GetService
                // inside a request process yields `undefined` and dies with {badmap,undefined}.
                // The portable one behaves identically and survives the process hop.
                if loggingConfigured then
                    services.AddSingleton<ILogger>(PortableLogger.create "Giraffe" level)

                // Snapshot the services as a plain list of (key, descriptor) pairs. Both the
                // ServiceCollection and the Dictionary behind it are process-dict refs, so the
                // collection itself cannot travel; a list of tuples of a single-case union is an
                // ordinary term and can. GiraffeHandler rebuilds a collection from it per
                // request, in the process that will actually read it.
                //
                // NOTE this makes the CONTAINER portable, not the values inside it. A service
                // that is itself a class with mutable fields is still a dead ref on the far
                // side — on BEAM, registered services must be immutable values (records, funs,
                // object expressions). See FOLLOWUPS.md.
                let serviceSnapshot =
                    services.Services
                    |> Seq.map (fun kv -> kv.Key, kv.Value)
                    |> List.ofSeq

                // The access log gets its own category, matching the Python backend's separate
                // "GiraffeMiddleware" logger.
                let accessLogger = PortableLogger.create "GiraffeHandler" level

                // Catch-all: every remaining path → middleware module with the composed pipeline,
                // the portable service snapshot and the access logger as state.
                let catchAllRoute =
                    CowboyRouter.route "/[...]" CowboyFFI.middlewareAtom (func, serviceSnapshot, accessLogger)

                let hostRule =
                    CowboyRouter.hostRule CowboyRouter.wildcard (staticRoutes @ [ catchAllRoute ])

                let dispatch = CowboyRouter.compile [ hostRule ]

                let transportOpts = Cowboy.tcpPort port
                let protoOpts = Cowboy.protocolOpts dispatch

                Cowboy.startClear CowboyFFI.httpAtom transportOpts protoOpts
                |> ignore

                Fable.Beam.Io.format "Starting Giraffe on port ~p~n" [ box port ]

    member this.Configure(configureApp: Action<IApplicationBuilder>) =
        (this :> IWebHostBuilder).Configure(configureApp)
        |> ignore

        this

    member this.Build(port: int) = (this :> IWebHostBuilder).Build(port)

    member this.ConfigureLogging(configureLogging: Action<ILoggingBuilder>) =
        Logging.configure loggerFactory services configureLogging
        loggingConfigured <- true
        this

    /// Log via Fable.Logging's BEAM (logger/OTP) provider — the BEAM counterpart of the
    /// Python backend's UseStructlog and the JS backend's UseConsoleLogging.
    member this.UseBeamLogging() =
        this.ConfigureLogging(fun builder -> builder.AddProvider(new Fable.Logging.Beam.LoggerProvider()))

module Host =
    let CreateDefaultBuilder (_: string array) = WebHostBuilder()
