namespace Fable.Giraffe

open System.Threading.Tasks
open Fable.Core
open Fable.Beam.Cowboy.CowboyReq

module CowboyReq = Fable.Beam.Cowboy.CowboyReq
module CowboyHandler = Fable.Beam.Cowboy.CowboyHandler

/// Cowboy handler module.
/// Implements init/2 which:
/// 1. Creates HttpContext from the Cowboy Req
/// 2. Runs the Giraffe handler pipeline
/// 3. Sends the response via cowboy_req:reply
/// 4. Returns {ok, Req, State}
module GiraffeHandler =
    /// On BEAM, Task CE is a CPS alias for Async. Identity cast.
    [<Emit("$0")>]
    let private taskToAsync (t: Task<'a>) : Async<'a> = nativeOnly

    /// The Cowboy handler init callback.
    /// Called for every incoming request.
    let init (req: Req) (state: obj) : obj =
        // State is (handler, services) — see WebHost.Build.
        let handler, services = state :?> (HttpHandler * ServiceCollection)
        let func: HttpFunc = handler earlyReturn

        // Create the HttpContext wrapping the Cowboy request and give it the services
        // collection so handlers can resolve dependencies (logger, etc.) via GetService.
        let ctx = HttpContext(req)
        ctx.SetServices(services)

        // Run the handler pipeline synchronously.
        // On BEAM, Task CE is a CPS alias for Async — identity cast.
        let _result = func ctx |> taskToAsync |> Async.RunSynchronously

        // Send the response via cowboy_req:reply/4.
        // Always use reply/4 — Cowboy handles empty iolist body ([]) fine.
        // (Empty body [||] compiles to [] on BEAM which is valid iodata.)
        let status = ctx.Response.StatusCode
        let body: string = byteArrayToBinary ctx.Response.Body |> unbox
        let headerMap = Fable.Beam.Maps.ofList (ctx.Response.GetHeadersMap())
        let req2 = CowboyReq.reply status headerMap body req

        CowboyHandler.ok req2 state
