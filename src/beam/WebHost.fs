namespace Fable.Giraffe

open System
open System.Threading.Tasks
open Fable.Core
open Fable.Beam.Cowboy
module Cowboy = Fable.Beam.Cowboy.Cowboy
module CowboyRouter = Fable.Beam.Cowboy.CowboyRouter
open Fable.Giraffe.Pipelines

module CowboyFFI =
    /// Create an Erlang map #{env => #{dispatch => Dispatch}}
    [<Emit("#{ env => #{ dispatch => $0 } }")>]
    let makeProtocolOpts (dispatch: obj) : obj = nativeOnly

    /// Create transport opts with port: [{port, Port}]
    [<Emit("[{port, $0}]")>]
    let makeTransportOpts (port: int) : obj = nativeOnly

    /// Create the catch-all route tuple: {"/[...]", Handler, State}
    [<Emit("{<<\"/[...]\">>, $0, $1}")>]
    let makeCatchAllRoute (handler: obj) (state: obj) : obj = nativeOnly

    /// Create the host match: {'_', [Route]}
    [<Emit("{'_', $0}")>]
    let makeHostMatch (routes: obj) : obj = nativeOnly

    /// Atom for the listener name
    [<Emit("http")>]
    let httpAtom : obj = nativeOnly

    /// Reference to the middleware Erlang module atom
    [<Emit("middleware")>]
    let middlewareAtom : obj = nativeOnly

    /// Create a native Erlang list with one element: [$0]
    [<Emit("[$0]")>]
    let singletonList (x: obj) : obj = nativeOnly

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
                let catchAllRoute = CowboyFFI.makeCatchAllRoute CowboyFFI.middlewareAtom h
                let hostMatch = CowboyFFI.makeHostMatch (CowboyFFI.singletonList catchAllRoute)
                let dispatch = CowboyRouter.compile (CowboyFFI.singletonList hostMatch)

                let transportOpts = CowboyFFI.makeTransportOpts port
                let protoOpts = CowboyFFI.makeProtocolOpts dispatch

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
