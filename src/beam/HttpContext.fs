namespace Fable.Giraffe

open System
open System.Collections.Generic
open System.Threading.Tasks

open Fable.Core
open Fable.Giraffe.Json
open Fable.Beam.Cowboy.CowboyReq

module CowboyReq = Fable.Beam.Cowboy.CowboyReq
module Maps = Fable.Beam.Maps

/// A body pre-buffered into the request map under `giraffe_body`. The in-process test harness
/// seeds it there because it has no live socket for `cowboy_req:read_body` to stream from; real
/// Cowboy requests never carry this key, so production always streams.
module private BufferedBody =

    [<Emit("maps:is_key(giraffe_body, $0)")>]
    let has (req: Req) : bool = nativeOnly

    [<Emit("maps:get(giraffe_body, $0)")>]
    let get (req: Req) : string = nativeOnly

/// HTTP request backed by a Cowboy request object.
type HttpRequest(req: Req) =
    member x.Path: string option = CowboyReq.path req |> Some

    member x.Method: string = CowboyReq.method' req

    member x.Protocol: string = CowboyReq.scheme req

    /// The request headers as Cowboy parsed them: one entry per name, names already lowercase.
    ///
    /// Cowboy is the normalising layer here — a client's `Authorization` arrives as
    /// `authorization`, and repeated headers are folded into a single comma-joined value — so
    /// this is a plain map read rather than a reimplementation of header semantics.
    member private x.HeaderPairs = Maps.toArray (CowboyReq.headers req)

    member x.GetTypedHeaders() : RequestHeaders =
        // RequestHeaders reads rows of [name; value; ...], the same shape the Python/ASGI
        // backend produces from `scope["headers"]`. Cowboy's map holds one value per name, so
        // every row here is exactly two elements — matching Python, where HeaderDictionary.Scoped
        // likewise joins multiple values into one string before the row is built.
        let rows = ResizeArray<ResizeArray<string>>()

        // An explicit loop, not Seq.map: on fable-beam, Seq.map over a ResizeArray passes a Ref
        // where a list is expected (see HttpResponse below).
        for (name, value) in x.HeaderPairs do
            rows.Add(ResizeArray([ name; value ]))

        RequestHeaders(rows)

    member x.GetBodyAsync() =
        task {
            if BufferedBody.has req then
                return BufferedBody.get req
            else
                let (_ok, body, _req2) = CowboyReq.readBody req
                return body
        }

    /// invariant: keys are lowercase, because Cowboy lowercases them. HeaderDictionary's indexer
    ///            lowercases the lookup key too, so `headers["Content-Type"]` still resolves.
    member x.Headers =
        let dict = Dictionary<string, string>()

        for (name, value) in x.HeaderPairs do
            dict[name] <- value

        HeaderDictionary(dict)

/// HTTP response that accumulates state before sending via cowboy_req:reply.
/// Uses mutable F# list for headers — avoids fable-beam ResizeArray/Seq
/// incompatibility (Seq.map on ResizeArray passes Ref instead of list).
type HttpResponse() =
    let mutable statusCode = None
    let mutable responseHeaders: (string * obj) list = []
    let mutable body: byte array = [||]

    member x.Headers =
        let dict = Dictionary<string, string>()

        for (k, v) in responseHeaders do
            dict[k] <- string v

        HeaderDictionary(dict)

    member val HasStarted: bool = false with get, set

    member x.StatusCode
        with get () =
            match statusCode with
            | Some sc -> sc
            | None -> 404
        and set (value: int) = statusCode <- Some value

    member x.Body = body

    member x.Clear() =
        responseHeaders <- []
        body <- [||]

    member x.WriteAsync(bytes: byte[]) =
        task {
            body <- bytes

            if not x.HasStarted then
                match statusCode with
                | Some _ -> ()
                | None -> statusCode <- Some 200

                x.HasStarted <- true
        }

    member x.SetHttpHeader(key: string, value: obj) =
        responseHeaders <- (key, value.ToString() :> obj) :: responseHeaders

    member x.SetStatusCode(status: int) = statusCode <- Some status

    member x.Redirect(location: string, permanent: bool) =
        let sc = if permanent then 301 else 302
        x.SetStatusCode(sc)
        x.SetHttpHeader("Location", location)

    /// Get accumulated headers as a list of (name, value) string tuples.
    member x.GetHeadersMap() : (string * string) list =
        responseHeaders
        |> List.map (fun (k, v) -> (k, string v))

type HttpContext(req: Req) =
    let items = Dictionary<string, obj>()
    let scope = Dictionary<string, obj>()
    let request = HttpRequest(req)
    let response = HttpResponse()

    /// The original Cowboy request object, needed for reply.
    member _.CowboyReq = req
    member _.Items = items
    member _.Request = request
    member _.Response = response

    member _.RequestServices = scope["services"] :?> ServiceCollection

    member ctx.ReadBodyFromRequestAsync() : Task<string> =
        task {
            // Cowboy's read_body returns the body as a binary (string) already.
            let! body = ctx.Request.GetBodyAsync()
            return body
        }

    /// <remarks>
    /// Decodes through TypedJson rather than casting the parsed value. The old
    /// <c>deserialize |&gt; unbox&lt;'T&gt;</c> handed back the raw Erlang map, not a record.
    /// Prefer <c>Core.validateJson</c>, which answers 422 with the field errors.
    /// </remarks>
    member inline x.BindJsonAsync<'T>() =
        task {
            // Cowboy's read_body returns the body as a binary (string) already.
            let! body = x.Request.GetBodyAsync()

            match tryDeserialize<'T> body with
            | Ok value -> return value
            | Error errs -> return failwith (Fable.TypedJson.Schema.formatErrors errs)
        }

    /// Set the services collection on this context (called by the middleware).
    member x.SetServices(services: ServiceCollection) = scope["services"] <- services
