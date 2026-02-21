namespace Fable.Giraffe

open System.Threading.Tasks
open Fable.Core
open Fable.Giraffe.CowboyReq

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

    /// Erlang maps:from_list — converts list of tuples to map
    [<Emit("maps:from_list($0)")>]
    let private mapsFromList (pairs: obj) : obj = nativeOnly

    /// Return {ok, Req, State} for Cowboy handler
    [<Emit("{ok, $0, $1}")>]
    let private okTuple (req: obj) (state: obj) : obj = nativeOnly

    /// The Cowboy handler init callback.
    /// Called for every incoming request.
    let init (req: Req) (state: obj) : obj =
        // Extract handler from state
        let handler = state :?> HttpHandler
        let func: HttpFunc = handler earlyReturn

        // Create the HttpContext wrapping the Cowboy request
        let ctx = HttpContext(req)

        // Run the handler pipeline synchronously.
        // On BEAM, Task CE is a CPS alias for Async — identity cast.
        let _result =
            func ctx
            |> taskToAsync
            |> Async.RunSynchronously

        // Send the response via cowboy_req:reply/4.
        // Always use reply/4 — Cowboy handles empty iolist body ([]) fine.
        // (Empty body [||] compiles to [] on BEAM which is valid iodata.)
        let status = ctx.Response.StatusCode
        let body = byteArrayToBinary ctx.Response.Body
        let headerMap = mapsFromList (ctx.Response.GetHeadersMap())
        let req2 = CowboyReq.reply status headerMap body req

        okTuple req2 state
