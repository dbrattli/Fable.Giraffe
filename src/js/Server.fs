namespace Fable.Giraffe

open System
open System.Collections.Generic
open System.Threading.Tasks

open Fable.Core

/// Node.js HTTP adapter.
///
/// The shared `HttpContext` is ASGI-shaped (it reads a `scope` dictionary and
/// talks to `receive`/`send` callbacks). Node's `http` server — and every
/// Connect/Express middleware — instead works with `(req, res, next)`. This
/// module is the shim between the two, which is why the JS backend reuses the
/// *same* `HttpContext.fs` as the Python (ASGI) backend with no Node-native
/// context type.
///
/// The Giraffe pipeline is exposed as a Connect-style middleware
/// `(req, res, next) -> unit`. That is the convention the entire Node middleware
/// ecosystem (cors, helmet, morgan, passport, OpenTelemetry, …) is written to,
/// so users get ecosystem reuse by mounting Giraffe into `express`/`connect`
/// WITHOUT Express being a dependency here:
///
///     app.use(cors())
///     app.use(helmet())
///     app.use(builder.ToMiddleware())   // Giraffe as terminal handler
///
/// When no Giraffe route matches, the pipeline calls `next()` so the host can
/// continue its own chain. The zero-dependency `run` path instead answers 404.
module Server =

    // --- Minimal node:http bindings ---------------------------------------

    [<AllowNullLiteral>]
    type IncomingMessage =
        abstract method: string
        abstract url: string
        abstract headers: obj

    [<AllowNullLiteral>]
    type ServerResponse =
        abstract statusCode: int with get, set
        abstract setHeader: string * string -> unit
        abstract ``end``: obj -> unit

    [<AllowNullLiteral>]
    type HttpServer =
        abstract listen: int * (unit -> unit) -> unit

    [<Import("createServer", "node:http")>]
    let private createServer (handler: Func<IncomingMessage, ServerResponse, unit>) : HttpServer = nativeOnly

    // --- Node <-> ASGI translation ----------------------------------------

    /// Collect the request stream into a single byte array (Promise-backed).
    /// NOTE: consumes the stream once — a prior body-parsing middleware that
    /// already drained it will leave this empty (documented interop gotcha).
    [<Emit("new Promise((resolve, reject) => { const chunks = []; $0.on('data', (c) => chunks.push(c)); $0.on('end', () => resolve(new Uint8Array(Buffer.concat(chunks)))); $0.on('error', reject); })")>]
    let private readBodyRaw (req: IncomingMessage) : Task<byte[]> = nativeOnly

    /// Node header value is a string (or string[] for e.g. set-cookie); flatten.
    let private headerValue (v: obj) : string =
        match v with
        | :? (string[]) as arr -> String.Join(", ", arr)
        | _ -> string v

    let private pathOf (url: string) =
        match url.IndexOf('?') with
        | -1 -> url
        | i -> url.Substring(0, i)

    let private buildScope (req: IncomingMessage) (services: ServiceCollection) : Scope =
        let headers =
            JS.Constructors.Object.entries req.headers
            |> Seq.map (fun (k, v) -> ResizeArray([ k; headerValue v ]))
            |> ResizeArray

        Dictionary<string, obj>(
            dict [
                "type", box "http"
                "method", box (req.method.ToUpper())
                "path", box (pathOf req.url)
                "headers", box headers
                "services", box services
            ]
        )

    let private makeReceive (req: IncomingMessage) : ReceiveAsync =
        fun () -> task {
            let! body = readBodyRaw req
            return Dictionary<string, obj>(dict [ "body", box body ])
        }

    let private makeSend (res: ServerResponse) : SendAsync =
        fun (msg: Request) -> task {
            match msg["type"] :?> string with
            | "http.response.start" ->
                res.statusCode <- msg["status"] :?> int
                let headers = msg["headers"] :?> ResizeArray<string * obj>

                for (k, v) in headers do
                    res.setHeader (k, string v)
            | "http.response.body" -> res.``end`` (box (msg["body"] :?> byte[]))
            | _ -> ()
        }

    /// Run the Giraffe handler for a single request. If the pipeline never
    /// wrote a response, `onUnhandled` decides what happens (call the host's
    /// `next`, or answer 404 in standalone mode).
    ///
    /// The handler is applied fully here — `handler earlyReturn ctx` — rather
    /// than pre-applied to a stored `HttpFunc`. `HttpHandler = HttpFunc ->
    /// HttpFunc` is Fable's ambiguous "function returning a function" case (see
    /// fable.io "automatic uncurrying"): a *partial* application stored as a
    /// value gets mis-normalized (via `curry2`) so it returns a function
    /// instead of running. A full, typed application is the shape Fable
    /// uncurries consistently, independent of how the handler was composed.
    let private dispatch (handler: HttpHandler) (services: ServiceCollection) (req: IncomingMessage) (res: ServerResponse) (onUnhandled: unit -> unit) : unit =
        let ctx = HttpContext(buildScope req services, makeReceive req, makeSend res)

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

        (createServer listener)
            .listen (port, (fun () -> printfn "Giraffe listening on http://localhost:%d" port))
