namespace Fable.Giraffe

open System
open System.Threading.Tasks
open Fable.Core
open Fable.Beam
open Fable.Beam.Cowboy
module Cowboy = Fable.Beam.Cowboy.Cowboy
module CowboyRouter = Fable.Beam.Cowboy.CowboyRouter
open Fable.Giraffe.Pipelines

module CowboyFFI =
    /// Atom for the listener name.
    [<Emit("http")>]
    let httpAtom: Atom = nativeOnly

    /// The Erlang module atom implementing the cowboy_handler behaviour
    /// (Middleware.fs compiles to the `middleware` module).
    [<Emit("middleware")>]
    let middlewareAtom: Atom = nativeOnly

type IApplicationBuilder =
    abstract ApplicationServices: ServiceCollection with get, set
    abstract UseGiraffe: HttpHandler -> unit

type IWebHostBuilder =
    abstract Configure: Action<IApplicationBuilder> -> IWebHostBuilder
    abstract Build: int -> unit

type WebHostBuilder() =
    let services = ServiceCollection()
    let mutable handler: HttpHandler option = None

    interface IWebHostBuilder with
        member this.Configure(configureApp: Action<IApplicationBuilder>) =
            let app =
                { new IApplicationBuilder with
                    member _.ApplicationServices
                        with get () = services
                        and set _ = ()

                    member _.UseGiraffe(h: HttpHandler) =
                        handler <- Some h
                }

            configureApp.Invoke(app)
            this

        member this.Build(port: int) =
            match handler with
            | None -> failwith "No handler configured. Call UseGiraffe in Configure."
            | Some h ->
                // Build Cowboy routing dispatch: all paths → middleware module with handler as state
                let catchAllRoute = CowboyRouter.route "/[...]" CowboyFFI.middlewareAtom h
                let hostRule = CowboyRouter.hostRule CowboyRouter.wildcard [ catchAllRoute ]
                let dispatch = CowboyRouter.compile [ hostRule ]

                let transportOpts = Cowboy.tcpPort port
                let protoOpts = Cowboy.protocolOpts dispatch

                Cowboy.startClear CowboyFFI.httpAtom transportOpts protoOpts |> ignore
                Fable.Beam.Io.format "Starting Giraffe on port ~p~n" [ box port ]

    member this.Configure(configureApp: Action<IApplicationBuilder>) =
        (this :> IWebHostBuilder).Configure(configureApp) |> ignore
        this

    member this.Build(port: int) =
        (this :> IWebHostBuilder).Build(port)

module Host =
    let CreateDefaultBuilder (_: string array) =
        WebHostBuilder()
