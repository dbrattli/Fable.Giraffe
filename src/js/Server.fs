namespace Fable.Giraffe

open System

open Fable.Core

/// Node.js HTTP host for Giraffe.
///
/// The Giraffe pipeline is exposed as a Connect-style middleware
/// `(req, res, next) -> unit` — the convention the entire Node middleware
/// ecosystem (cors, helmet, morgan, passport, OpenTelemetry, …) is written to.
/// Mount Giraffe into `express`/`connect` for ecosystem reuse WITHOUT Express
/// being a dependency here:
///
///     app.use(cors())
///     app.use(helmet())
///     app.use(builder.ToMiddleware())   // Giraffe as terminal handler
///
/// When no Giraffe route matches, the pipeline calls `next()` so the host can
/// continue its own chain. The zero-dependency `run` path answers 404 instead.
module Server =

    [<AllowNullLiteral>]
    type HttpServer =
        abstract listen: int * (unit -> unit) -> unit

    [<Import("createServer", "node:http")>]
    let private createServer (handler: Func<IncomingMessage, ServerResponse, unit>) : HttpServer = nativeOnly

    /// Run the Giraffe handler for a single request. If the pipeline never wrote
    /// a response, `onUnhandled` decides what happens (call the host's `next`, or
    /// answer 404 in standalone mode).
    ///
    /// The handler is applied fully here — `handler earlyReturn ctx` — rather
    /// than pre-applied to a stored `HttpFunc`. `HttpHandler = HttpFunc ->
    /// HttpFunc` is Fable's ambiguous "function returning a function" case (see
    /// fable.io "automatic uncurrying"): a *partial* application stored as a
    /// value gets mis-normalized (via `curry2`) so it returns a function instead
    /// of running. A full, typed application is the shape Fable uncurries
    /// consistently, independent of how the handler was composed.
    let private dispatch
        (handler: HttpHandler)
        (services: ServiceCollection)
        (req: IncomingMessage)
        (res: ServerResponse)
        (onUnhandled: unit -> unit)
        : unit =
        let ctx = HttpContext(req, res, services)

        (task {
            try
                let! _ = handler earlyReturn ctx

                if not ctx.Response.HasStarted then
                    onUnhandled ()
            with ex ->
                if not ctx.Response.HasStarted then
                    res.statusCode <- 500
                    res.setHeader ("content-type", "text/plain; charset=utf-8")
                    res.``end`` (box (sprintf "Internal Server Error: %s" ex.Message))
        })
        |> ignore

    /// Expose the Giraffe handler as a Connect/Express middleware
    /// `(req, res, next) -> unit`. Mount with `app.use(...)` after any other
    /// Node middleware; unmatched requests fall through via `next()`.
    let toMiddleware (handler: HttpHandler) (services: ServiceCollection) : Func<IncomingMessage, ServerResponse, (unit -> unit), unit> =
        Func<_, _, _, _>(fun req res next -> dispatch handler services req res next)

    /// Start a zero-dependency `http` server driving the Giraffe handler.
    /// Unmatched requests get a 404 (there is no outer host to fall through to).
    let run (handler: HttpHandler) (services: ServiceCollection) (port: int) : unit =
        let listener =
            Func<IncomingMessage, ServerResponse, unit>(fun req res ->
                let send404 () =
                    res.statusCode <- 404
                    res.setHeader ("content-type", "text/plain; charset=utf-8")
                    res.``end`` (box "Not Found")

                dispatch handler services req res send404)

        (createServer listener).listen (port, (fun () -> printfn "Giraffe listening on http://localhost:%d" port))
