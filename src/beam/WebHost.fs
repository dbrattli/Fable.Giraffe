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

type IApplicationBuilder =
    abstract ApplicationServices: ServiceCollection with get, set
    abstract UseGiraffe: HttpHandler -> unit

type IWebHostBuilder =
    abstract Configure: Action<IApplicationBuilder> -> IWebHostBuilder
    abstract Build: int -> unit

type WebHostBuilder() =
    let loggerFactory = LoggerFactory.Create()
    let services = ServiceCollection()
    let mutable handler: HttpHandler option = None

    interface IWebHostBuilder with
        member this.Configure(configureApp: Action<IApplicationBuilder>) =
            let app =
                { new IApplicationBuilder with
                    member _.ApplicationServices
                        with get () = services
                        and set _ = ()

                    member _.UseGiraffe(h: HttpHandler) = handler <- Some h }

            configureApp.Invoke(app)
            this

        member this.Build(port: int) =
            match handler with
            | None -> failwith "No handler configured. Call UseGiraffe in Configure."
            | Some h ->
                // Build Cowboy routing dispatch: all paths → middleware module with the handler
                // AND the services collection as state, so the request handler can resolve
                // services (logger, etc.) off the context via GetService.
                let catchAllRoute =
                    CowboyRouter.route "/[...]" CowboyFFI.middlewareAtom (h, services)

                let hostRule = CowboyRouter.hostRule CowboyRouter.wildcard [ catchAllRoute ]
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
        this

    /// Log via Fable.Logging's BEAM (logger/OTP) provider — the BEAM counterpart of the
    /// Python backend's UseStructlog and the JS backend's UseConsoleLogging.
    member this.UseBeamLogging() =
        this.ConfigureLogging(fun builder -> builder.AddProvider(new Fable.Logging.Beam.LoggerProvider()))

module Host =
    let CreateDefaultBuilder (_: string array) = WebHostBuilder()
