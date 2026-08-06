namespace Fable.Giraffe

open System.Threading.Tasks
open Fable.Core
open Fable.Beam
open Fable.Beam.Cowboy.CowboyReq
open Fable.Logging

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

    [<Emit("?MODULE")>]
    let private selfModule () : Atom = nativeOnly

    /// This module's own Erlang atom, for the caller that routes Cowboy here.
    ///
    /// Fable derives generated BEAM module names from the source path, and that path depends on
    /// how the project is consumed: compiled standalone this file becomes
    /// `fable_giraffe_beam_middleware`, referenced from a sibling app it becomes
    /// `src_beam_middleware`, and referenced from another repository it becomes
    /// `fable_giraffe_src_beam_middleware`. A hand-written atom cannot track that, and a wrong
    /// one fails at the first request with `undef` on `init/2` rather than at startup.
    ///
    /// `?MODULE` is expanded by the Erlang preprocessor in the file the text appears in, and
    /// `[<Emit>]` is inlined at the *call site* — so this has to live here and be reached
    /// through an ordinary cross-module call. Do not mark it `inline`, and do not lift the
    /// `[<Emit>]` into a value that WebHost references: either would expand `?MODULE` in the
    /// caller and yield the caller's name.
    let moduleAtom () : Atom = selfModule ()

    /// Monotonic clock for the access log. Fable.Beam's `Erlang.monotonicTimeMs` is
    /// millisecond-resolution, which rounds most handler runs to 0 — read microseconds so the
    /// logged duration matches the Python backend's fractional milliseconds.
    [<Emit("erlang:monotonic_time(microsecond)")>]
    let private monotonicTimeUs () : int64 = nativeOnly

    /// The Cowboy handler init callback.
    /// Called for every incoming request.
    let init (req: Req) (state: obj) : obj =
        // State is (func, serviceSnapshot, logger) — the pipeline is composed once in
        // WebHost.Build, so init/2 (called by Cowboy for every request) does no per-request
        // handler composition.
        //
        // Cowboy runs every request in a fresh process, and Fable compiles a class with mutable
        // fields to a process-dictionary ref — so neither a ServiceCollection nor a
        // Fable.Logging logger built in the builder process can be read here. Hence a plain list
        // of (key, descriptor) pairs plus an object-expression logger, both ordinary terms.
        let func, serviceSnapshot, logger =
            state :?> (HttpFunc * (string * ServiceDescriptor) list * ILogger)

        // Rebuild the service collection HERE, so its backing ref belongs to this process and
        // ctx.GetService<_>() resolves. Cheap: a handful of entries, and only the dictionary is
        // rebuilt — the registered instances are shared, not copied.
        let services = ServiceCollection()

        for key, descriptor in serviceSnapshot do
            services.Services[key] <- descriptor

        let ctx = HttpContext(req)
        ctx.SetServices(services)

        // Only read the clock when the access log will actually be emitted; otherwise this is a
        // per-request BIF call on the hot path for nothing.
        let logging = logger.IsEnabled LogLevel.Debug
        let start = if logging then monotonicTimeUs () else 0L

        // Run the handler pipeline synchronously.
        // On BEAM, Task CE is a CPS alias for Async — identity cast.
        let _result = func ctx |> taskToAsync |> Async.RunSynchronously

        if logging then
            let elapsedMs = double (monotonicTimeUs () - start) / 1000.0

            // Status -> level mirrors the Python backend: 2xx info, 3xx/4xx error, 5xx critical.
            let logLevel =
                match ctx.Response.StatusCode with
                | code when code < 300 -> LogLevel.Information
                | code when code < 500 -> LogLevel.Error
                | _ -> LogLevel.Critical

            logger.Log(
                logLevel,
                "Giraffe returned {Status} for {HttpProtocol} {HttpMethod} at {Path} in {ElapsedMs}",
                parameters =
                    [| ctx.Response.StatusCode :> obj
                       ctx.Request.Protocol
                       ctx.Request.Method
                       defaultArg ctx.Request.Path ""
                       elapsedMs |]
            )

        // Send the response via cowboy_req:reply/4.
        // Always use reply/4 — Cowboy handles empty iolist body ([]) fine.
        // (Empty body [||] compiles to [] on BEAM which is valid iodata.)
        let status = ctx.Response.StatusCode
        let body: string = byteArrayToBinary ctx.Response.Body |> unbox
        let headerMap = Fable.Beam.Maps.ofList (ctx.Response.GetHeadersMap())
        let req2 = CowboyReq.reply status headerMap body req

        CowboyHandler.ok req2 state
