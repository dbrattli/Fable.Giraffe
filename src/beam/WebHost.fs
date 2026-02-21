namespace Fable.Giraffe

open System
open System.Threading.Tasks
open Fable.Core
open Fable.Giraffe.Pipelines

module CowboyFFI =
    /// Cowboy router compile: cowboy_router:compile(Routes)
    [<Emit("cowboy_router:compile($0)")>]
    let cowboyRouterCompile (routes: obj) : obj = nativeOnly

    /// Start Cowboy clear (HTTP) listener.
    [<Emit("cowboy:start_clear($0, $1, $2)")>]
    let cowboyStartClear (name: obj) (transportOpts: obj) (protoOpts: obj) : obj = nativeOnly

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

    /// Erlang io:format with a single argument (native list)
    [<Emit("io:format($0, [$1])")>]
    let ioFormat1 (fmt: string) (arg: obj) : unit = nativeOnly

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
                let dispatch = CowboyFFI.cowboyRouterCompile (CowboyFFI.singletonList hostMatch)

                let transportOpts = CowboyFFI.makeTransportOpts port
                let protoOpts = CowboyFFI.makeProtocolOpts dispatch

                CowboyFFI.cowboyStartClear CowboyFFI.httpAtom transportOpts protoOpts |> ignore
                CowboyFFI.ioFormat1 "Starting Giraffe on port ~p~n" port

    member this.Configure(configureApp: Action<IApplicationBuilder>) =
        (this :> IWebHostBuilder).Configure(configureApp) |> ignore
        this

    member this.Build(port: int) =
        (this :> IWebHostBuilder).Build(port)

module Host =
    let CreateDefaultBuilder (_: string array) =
        WebHostBuilder()
