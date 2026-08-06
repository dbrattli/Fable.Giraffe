namespace Fable.Giraffe

open System
open Fable.Core
open Fable.Beam
open Fable.Beam.Cowboy
open Fable.Logging

module Cowboy = Fable.Beam.Cowboy.Cowboy
module CowboyRouter = Fable.Beam.Cowboy.CowboyRouter
module Application = Fable.Beam.Application

module CowboyFFI =
    /// Atom for the listener name.
    [<Emit("http")>]
    let httpAtom: Atom = nativeOnly

    /// The OTP application to start before the listener. Same text as `httpAtom`'s neighbour by
    /// coincidence only — one names our listener, this one names cowboy itself.
    [<Emit("cowboy")>]
    let cowboyAppAtom: Atom = nativeOnly

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

type IWebHostBuilder =
    abstract Configure: Action<IApplicationBuilder> -> IWebHostBuilder
    abstract Build: int -> unit

type WebHostBuilder() =
    let loggerFactory = LoggerFactory.Create()
    let services = ServiceCollection()
    let mutable handler: HttpHandler option = None
    let staticMounts = ResizeArray<string * string>()

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

                // Snapshot the services as a plain list of (key, descriptor) pairs. Both the
                // ServiceCollection and the Dictionary behind it are process-dict refs, so the
                // collection itself cannot travel; a list of tuples of a single-case union is an
                // ordinary term and can. GiraffeHandler rebuilds a collection from it per
                // request, in the process that will actually read it.
                //
                // NOTE this makes the CONTAINER portable, not the values inside it. A service
                // that is itself a class with mutable fields is still a dead ref on the far
                // side — on BEAM, registered services must be immutable values (records, funs,
                // object expressions). Fable.Logging's own loggers satisfy this as of 1.0.0.
                // See FOLLOWUPS.md.
                let serviceSnapshot =
                    services.Services
                    |> Seq.map (fun kv -> kv.Key, kv.Value)
                    |> List.ofSeq

                // The access log gets its own category, matching the Python backend's separate
                // "GiraffeMiddleware" logger. Created here, after every ConfigureLogging call:
                // Fable.Logging 1.0.0 loggers snapshot the factory's providers and level at
                // creation, so one built earlier would miss providers registered later.
                let accessLogger = loggerFactory.CreateLogger("GiraffeHandler")

                // Catch-all: every remaining path → middleware module with the composed pipeline,
                // the portable service snapshot and the access logger as state. The handler module
                // names itself (`GiraffeHandler.moduleAtom`) rather than being named here: the
                // generated module name varies with how this project is consumed, and a stale
                // hand-written atom only surfaces as `undef` at the first request.
                let catchAllRoute =
                    CowboyRouter.route "/[...]" (GiraffeHandler.moduleAtom ()) (func, serviceSnapshot, accessLogger)

                let hostRule =
                    CowboyRouter.hostRule CowboyRouter.wildcard (staticRoutes @ [ catchAllRoute ])

                let dispatch = CowboyRouter.compile [ hostRule ]

                let transportOpts = Cowboy.tcpPort port
                let protoOpts = Cowboy.protocolOpts dispatch

                // Cowboy and its dependencies (ranch, cowlib) are OTP applications and must be
                // running before start_clear. Hosts that boot us from an `erl -eval` often do this
                // themselves; doing it here means embedding Giraffe in an existing release works
                // too. ensure_all_started is idempotent, so a host that already did it pays nothing.
                let hostLogger = loggerFactory.CreateLogger("WebHost")

                match Application.ensureAllStarted CowboyFFI.cowboyAppAtom with
                | Ok _ -> ()
                | Error reason -> hostLogger.LogError("Could not start the cowboy application: {Reason}", reason)

                // Report through the logger, not stdout: a host may be sharing stdout with a REPL
                // or another interactive stream. And a discarded start_clear result means a busy
                // port looks like a successful boot that then serves nothing.
                match Cowboy.startClear CowboyFFI.httpAtom transportOpts protoOpts with
                | Ok _ -> hostLogger.LogInformation("Giraffe listening on port {Port}", port)
                | Error reason -> hostLogger.LogError("Giraffe failed to listen on port {Port}: {Reason}", port, reason)

    member this.Configure(configureApp: Action<IApplicationBuilder>) =
        (this :> IWebHostBuilder).Configure(configureApp)
        |> ignore

        this

    member this.Build(port: int) = (this :> IWebHostBuilder).Build(port)

    member this.ConfigureLogging(configureLogging: Action<ILoggingBuilder>) =
        Logging.configure loggerFactory services configureLogging
        this

    /// Log via Fable.Logging's BEAM (logger/OTP) provider — the BEAM counterpart of the
    /// Python backend's UseStructlog and the JS backend's UseConsoleLogging.
    member this.UseBeamLogging() =
        this.ConfigureLogging(fun builder -> builder.AddProvider(new Fable.Logging.Beam.LoggerProvider()))

module Host =
    let CreateDefaultBuilder (_: string array) = WebHostBuilder()
