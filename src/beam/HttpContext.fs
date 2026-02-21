namespace Fable.Giraffe

open System
open System.Collections.Generic
open System.Text
open System.Threading.Tasks

open Fable.Core
open Fable.Giraffe.Json
open Fable.Giraffe.CowboyReq

/// ASGI-compatible type aliases (kept for shared code compatibility)
type Scope = Dictionary<string, obj>
type Request = Dictionary<string, obj>
type Response = Dictionary<string, obj>

type ReceiveAsync = unit -> Task<Response>
type SendAsync = Request -> Task<unit>

/// Dummy ASGIApp type for shared code compatibility.
/// On BEAM, the middleware pattern is different (Cowboy handlers).
type ASGIApp = Func<Scope, ReceiveAsync, SendAsync, Task<unit>>


module HeaderNames =
    [<Literal>]
    let ContentType = "content-type"

    [<Literal>]
    let ContentLength = "content-length"

module HttpMethods =
    [<Literal>]
    let Head = "head"

    let IsGet (method: string) = method = "GET"
    let IsPost (method: string) = method = "POST"
    let IsPatch (method: string) = method = "PATCH"
    let IsPut (method: string) = method = "PUT"
    let IsDelete (method: string) = method = "DELETE"
    let IsHead (method: string) = method = "HEAD"
    let IsOptions (method: string) = method = "OPTIONS"
    let IsTrace (method: string) = method = "TRACE"
    let IsConnect (method: string) = method = "CONNECT"

type HeaderDictionary(headers: Dictionary<string, StringValues>) =
    new(headers: Dictionary<string, string>) =
        let dict =
            headers
            |> Seq.map (fun (KeyValue (k, v)) -> (k, StringValues v))
            |> dict

        HeaderDictionary(Dictionary(dict))

    new() = HeaderDictionary(Dictionary<string, StringValues>())

    member x.Item(key: string) = headers[key.ToLower()]

    member x.Add(key: string, value: string) =
        headers[key.ToLower()] <- StringValues(value)

    member x.Add(key: string, value: StringValues) = headers[key.ToLower()] <- value

    member x.Scoped =
        headers
        |> Seq.map (fun (KeyValue (k, v)) -> ResizeArray([ k; String.Join(", ", v.ToArray()) ]))
        |> ResizeArray


type StringSegment(value: string) =
    member x.Value = value
    override x.ToString() = value
    static member Empty = StringSegment("")

[<AllowNullLiteral>]
type MediaTypeHeaderValue(value: string) =
    let parts = value.Split(';')
    let mediaType = parts[ 0 ].Trim()

    let charset =
        parts
        |> Array.tryFind (fun p -> p.Trim().StartsWith("charset="))

    let charset =
        charset
        |> Option.map (fun c -> c.Split('=').[1].Trim())

    member x.MediaType = StringSegment(mediaType)
    member x.Quality = Nullable 1.0
    member x.Charset = charset

    override x.ToString() = value

type RequestHeaders(headers: ResizeArray<ResizeArray<string>>) =
    member x.Accept
        with get () =
            let found =
                headers
                |> Seq.tryFind (fun x -> x[ 0 ].ToLower() = "accept")

            match found with
            | Some value ->
                value
                |> Seq.skip 1
                |> Seq.map MediaTypeHeaderValue
                |> ResizeArray
            | _ -> ResizeArray<MediaTypeHeaderValue>()

        and set (_value: ResizeArray<MediaTypeHeaderValue>) = failwith "Not implemented"

/// HTTP request backed by a Cowboy request object.
type HttpRequest(req: Req) =
    member x.Path: string option = CowboyReq.path req |> Some

    member x.Method: string = CowboyReq.method' req

    member x.Protocol: string = CowboyReq.scheme req

    member x.GetTypedHeaders() : RequestHeaders =
        // Convert Cowboy headers map to the expected format
        RequestHeaders(ResizeArray())

    member x.GetBodyAsync() = task {
        let (_ok, body, _req2) = CowboyReq.readBody req
        return body
    }

    member x.Headers =
        HeaderDictionary()

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

    member x.WriteAsync(bytes: byte[]) = task {
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

    /// Get accumulated headers as a list of tuples.
    member x.GetHeadersMap() : obj =
        responseHeaders :> obj

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

    member ctx.WriteBytesAsync(bytes: byte[]) = task {
        ctx.SetHttpHeader(HeaderNames.ContentLength, len bytes)

        if ctx.Request.Method <> HttpMethods.Head then
            do! ctx.Response.WriteAsync(bytes)

        return Some ctx
    }

    member ctx.SetStatusCode(statusCode: int) = ctx.Response.SetStatusCode(statusCode)

    member ctx.SetHttpHeader(key: string, value: obj) = ctx.Response.SetHttpHeader(key, value)

    member ctx.SetContentType(contentType: string) =
        ctx.SetHttpHeader(HeaderNames.ContentType, contentType)

    member ctx.ReadBodyFromRequestAsync() : Task<string> = task {
        let! bytes = ctx.Request.GetBodyAsync()
        return bytes |> Encoding.UTF8.GetString
    }

    member inline x.BindJsonAsync<'T>() = task {
        let! body = x.Request.GetBodyAsync()

        return
            body
            |> Encoding.UTF8.GetString
            |> deserialize
            |> unbox<'T>
    }

    member inline x.GetService<'T>() : 'T =
        let (Singleton service) = x.RequestServices.GetService(typeof<'T>)
        service :?> 'T

    /// Set the services collection on this context (called by the middleware).
    member x.SetServices(services: ServiceCollection) =
        scope["services"] <- services
