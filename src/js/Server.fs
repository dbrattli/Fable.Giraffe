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

    /// A Connect-style middleware `(req, res, next) -> unit`.
    type private Middleware = Func<IncomingMessage, ServerResponse, (unit -> unit), unit>

    /// `serve-static` — the library behind `express.static`. We delegate the whole static-file
    /// problem to it (content-type detection, ETags, Range requests, conditional requests,
    /// Cache-Control); on a miss it invokes `next`, so the request falls through to Giraffe.
    [<Import("default", "serve-static")>]
    let private serveStatic (root: string) : Middleware = nativeOnly

    /// Node's `IncomingMessage.url` is mutable and serve-static matches against it. To mount a
    /// directory under a URL prefix we strip the prefix before delegating (serve-static, unlike
    /// `express`, does not know about mount paths) and restore it if the file isn't found.
    [<Emit("$0.url = $1")>]
    let private setUrl (req: IncomingMessage) (url: string) : unit = nativeOnly

    /// Compose the configured static mounts into a single pre-Giraffe middleware. Each
    /// `(requestPath, directory)` is tried in order; the first that serves a file ends the
    /// request, otherwise `next` runs (the next mount, and finally Giraffe). An empty
    /// `requestPath` mounts at the root — the request path is passed to serve-static as-is.
    let private staticPipeline (mounts: (string * string) list) : Middleware =
        let stages =
            mounts
            |> List.map (fun (prefix, dir) -> prefix, serveStatic dir)

        Func<_, _, _, _>(fun (req: IncomingMessage) (res: ServerResponse) (next: unit -> unit) ->
            let rec go stages =
                match stages with
                | [] -> next ()
                | (prefix: string, mw: Middleware) :: rest ->
                    if prefix = "" then
                        mw.Invoke(req, res, (fun () -> go rest))
                    elif
                        req.url = prefix
                        || req.url.StartsWith(prefix + "/")
                    then
                        let original = req.url
                        setUrl req (req.url.Substring(prefix.Length))

                        mw.Invoke(
                            req,
                            res,
                            (fun () ->
                                setUrl req original
                                go rest)
                        )
                    else
                        go rest

            go stages)

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
    ///
    /// Configured static mounts run first (serve-static), then Giraffe, then the host's `next`.
    let toMiddleware
        (mounts: (string * string) list)
        (handler: HttpHandler)
        (services: ServiceCollection)
        : Func<IncomingMessage, ServerResponse, (unit -> unit), unit> =
        let statics = staticPipeline mounts

        Func<_, _, _, _>(fun req res next -> statics.Invoke(req, res, (fun () -> dispatch handler services req res next)))

    /// Start a zero-framework `http` server driving the Giraffe handler. Configured static
    /// mounts (serve-static) are tried first; unmatched requests get a 404 (there is no outer
    /// host to fall through to).
    let run (mounts: (string * string) list) (handler: HttpHandler) (services: ServiceCollection) (port: int) : unit =
        let statics = staticPipeline mounts

        let listener =
            Func<IncomingMessage, ServerResponse, unit>(fun req res ->
                let send404 () =
                    res.statusCode <- 404
                    res.setHeader ("content-type", "text/plain; charset=utf-8")
                    res.``end`` (box "Not Found")

                statics.Invoke(req, res, (fun () -> dispatch handler services req res send404)))

        (createServer listener).listen (port, (fun () -> printfn "Giraffe listening on http://localhost:%d" port))
