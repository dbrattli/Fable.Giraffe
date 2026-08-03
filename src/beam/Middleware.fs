namespace Fable.Giraffe

open System.Threading.Tasks
open Fable.Core
open Fable.Beam.Cowboy.CowboyReq

module CowboyReq = Fable.Beam.Cowboy.CowboyReq
module CowboyHandler = Fable.Beam.Cowboy.CowboyHandler
module Logger = Fable.Beam.Logger

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

    /// Monotonic clock for the access log. Fable.Beam's `Erlang.monotonicTimeMs` is
    /// millisecond-resolution, which rounds most handler runs to 0 — read microseconds so the
    /// logged duration matches the Python backend's fractional milliseconds.
    [<Emit("erlang:monotonic_time(microsecond)")>]
    let private monotonicTimeUs () : int64 = nativeOnly

    /// The Cowboy handler init callback.
    /// Called for every incoming request.
    let init (req: Req) (state: obj) : obj =
        // State is (func, services, accessLogEnabled) — the pipeline is composed once in
        // WebHost.Build, so init/2 (called by Cowboy for every request) does no per-request
        // handler composition. `accessLogEnabled` is a bool rather than an ILogger on purpose:
        // Cowboy runs every request in a fresh process, and Fable compiles a class with mutable
        // fields to a process-dictionary ref, so a logger object built in the builder process
        // reads back as `undefined` here. See the note in WebHost.Build.
        let func, services, accessLogEnabled =
            state :?> (HttpFunc * ServiceCollection * bool)

        // Create the HttpContext wrapping the Cowboy request and give it the services
        // collection. NOTE: resolving off it via GetService does NOT currently work on BEAM —
        // ServiceCollection is ref-backed for the same reason as the logger above, so
        // `ctx.GetService<_>()` dies with {badmap,undefined} in this process. Tracked in
        // FOLLOWUPS.md; the assignment stays so the fix is a change of representation only.
        let ctx = HttpContext(req)
        ctx.SetServices(services)

        // Only read the clock when the access log will actually be emitted; otherwise this is a
        // per-request BIF call on the hot path for nothing.
        let start = if accessLogEnabled then monotonicTimeUs () else 0L

        // Run the handler pipeline synchronously.
        // On BEAM, Task CE is a CPS alias for Async — identity cast.
        let _result = func ctx |> taskToAsync |> Async.RunSynchronously

        if accessLogEnabled then
            let elapsedMs = double (monotonicTimeUs () - start) / 1000.0
            let statusCode = ctx.Response.StatusCode
            let path = defaultArg ctx.Request.Path ""

            // Same shape as the Python backend's access log, pre-rendered: OTP's logger is a
            // global service reachable from any process, unlike the ILogger object graph.
            let message =
                $"Giraffe returned %d{statusCode} for %s{ctx.Request.Protocol} %s{ctx.Request.Method} "
                + $"at %s{path} in %.3f{elapsedMs} ms"

            // Status -> level mirrors the Python backend: 2xx info, 3xx/4xx error, 5xx critical.
            if statusCode < 300 then Logger.logger.info message
            elif statusCode < 500 then Logger.logger.error message
            else Logger.logger.critical message

        // Send the response via cowboy_req:reply/4.
        // Always use reply/4 — Cowboy handles empty iolist body ([]) fine.
        // (Empty body [||] compiles to [] on BEAM which is valid iodata.)
        let status = ctx.Response.StatusCode
        let body: string = byteArrayToBinary ctx.Response.Body |> unbox
        let headerMap = Fable.Beam.Maps.ofList (ctx.Response.GetHeadersMap())
        let req2 = CowboyReq.reply status headerMap body req

        CowboyHandler.ok req2 state
