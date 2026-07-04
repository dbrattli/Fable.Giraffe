namespace Fable.Giraffe

open System
open System.Collections.Generic
open System.Text
open System.Threading.Tasks

open Fable.Core
open Fable.Giraffe.Json

// Node-native HTTP context for the JS backend.
//
// Like the BEAM backend wraps a Cowboy `Req`, this wraps Node's `http`
// `IncomingMessage`/`ServerResponse` directly. It does NOT emulate ASGI — the
// shared abstraction between backends is the `HttpContext` *member surface* that
// Core/Routing/Negotiation call, not any particular server protocol. This keeps
// `src/js` self-contained: it has no dependency on the Python backend, so the
// two evolve (and can be deleted) independently.

// --- Minimal node:http bindings ---------------------------------------------

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

[<AutoOpen>]
module private NodeInterop =
    /// Collect the request stream into a single byte array (Promise-backed).
    /// NOTE: consumes the stream once — a prior body-parsing middleware that
    /// already drained it leaves this empty (documented Express-interop gotcha).
    [<Emit("new Promise((resolve, reject) => { const chunks = []; $0.on('data', (c) => chunks.push(c)); $0.on('end', () => resolve(new Uint8Array(Buffer.concat(chunks)))); $0.on('error', reject); })")>]
    let readBody (req: IncomingMessage) : Task<byte[]> = nativeOnly

    /// Node header value is a string (or string[] for e.g. set-cookie); flatten.
    let headerValue (v: obj) : string =
        match v with
        | :? (string[]) as arr -> String.Join(", ", arr)
        | _ -> string v

    let pathOf (url: string) =
        match url.IndexOf('?') with
        | -1 -> url
        | i -> url.Substring(0, i)


// --- Node request/response wrappers -----------------------------------------

type HttpRequest(req: IncomingMessage) =
    member x.Path: string option = pathOf req.url |> Some

    member x.Method: string = req.method.ToUpper()

    member x.Protocol: string = "http"

    member x.GetTypedHeaders() : RequestHeaders =
        JS.Constructors.Object.entries req.headers
        |> Seq.map (fun (k, v) -> ResizeArray([ k; headerValue v ]))
        |> ResizeArray
        |> RequestHeaders

    member x.GetBodyAsync() : Task<byte[]> = readBody req

    member x.Headers =
        let dict = Dictionary<string, string>()

        for (k, v) in JS.Constructors.Object.entries req.headers do
            dict[k] <- headerValue v

        HeaderDictionary(dict)

/// Accumulates status + headers, then writes once to the Node response.
type HttpResponse(res: ServerResponse) =
    let headers = Dictionary<string, string>()
    let mutable statusCode: int option = None

    member val HasStarted: bool = false with get, set

    member x.StatusCode
        with get () = defaultArg statusCode 404
        and set (value: int) = statusCode <- Some value

    member x.Headers = HeaderDictionary(Dictionary(headers))

    member x.Clear() =
        headers.Clear()
        statusCode <- None

    member x.WriteAsync(bytes: byte[]) =
        task {
            if not x.HasStarted then
                let code = defaultArg statusCode 200
                statusCode <- Some code
                res.statusCode <- code

                for KeyValue(k, v) in headers do
                    res.setHeader (k, v)

                x.HasStarted <- true

            res.``end`` (box bytes)
        }

    member x.SetHttpHeader(key: string, value: obj) = headers[key] <- string value

    member x.SetStatusCode(status: int) = statusCode <- Some status

    member x.Redirect(location: string, permanent: bool) =
        x.SetStatusCode(if permanent then 301 else 302)
        x.SetHttpHeader("Location", location)

type HttpContext(req: IncomingMessage, res: ServerResponse, services: ServiceCollection) =
    let items = Dictionary<string, obj>()
    let request = HttpRequest(req)
    let response = HttpResponse(res)

    member _.Items = items
    member _.Request = request
    member _.Response = response
    member _.RequestServices = services

    member ctx.ReadBodyFromRequestAsync() : Task<string> =
        task {
            let! bytes = ctx.Request.GetBodyAsync()
            return bytes |> Encoding.UTF8.GetString
        }

    member inline x.BindJsonAsync<'T>() =
        task {
            let! body = x.Request.GetBodyAsync()

            return
                body
                |> Encoding.UTF8.GetString
                |> deserialize
                |> unbox<'T>
        }
