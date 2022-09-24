namespace Fable.Giraffe

open System
open System.Collections.Generic
open System.Text
open System.Threading.Tasks

open Fable.SimpleJson.Python


type Scope = Dictionary<string, obj>
type Request = Dictionary<string, obj>
type Response = Dictionary<string, obj>

/// https://asgi.readthedocs.io/
type ASGIApp = Func<Scope, unit -> Task<Response>, Request -> Task<unit>, Task<unit>>


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
        |> Seq.map (fun (KeyValue (k, v)) -> ResizeArray([ k; v.ToString() ]))
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
                |> Seq.map MediaTypeHeaderValue
                |> ResizeArray
            | _ -> ResizeArray<MediaTypeHeaderValue>()

        and set (value: ResizeArray<MediaTypeHeaderValue>) = failwith "Not implemented"

type HttpRequest(scope: Scope, receive: unit -> Task<Response>) =
    member x.Path: string option = scope["path"] :?> string |> Some

    member x.Method: string = scope["method"] :?> string

    member x.Protocol: string = scope["type"] :?> string

    member x.GetTypedHeaders() : RequestHeaders =
        RequestHeaders(scope["headers"] :?> ResizeArray<ResizeArray<string>>)

    member x.GetBodyAsync() = task {
        let! response = receive ()
        return response["body"] :?> byte array
    }

    member x.Headers =
        scope["headers"] :?> Dictionary<string, string>
        |> HeaderDictionary

type HttpResponse(send: Request -> Task<unit>) =
    let responseStart =
        Dictionary<string, obj>(
            dict [
                ("type", "http.response.start" :> obj)
                ("status", 200)
                ("headers", ResizeArray<_>())
            ]
        )

    let responseBody =
        Dictionary<string, obj>(dict [ ("type", "http.response.body" :> obj) ])

    member x.Headers =
        responseStart["headers"] :?> Dictionary<string, string>
        |> HeaderDictionary

    member val HasStarted: bool = false with get, set

    member x.StatusCode
        with get () =
            if x.HasStarted then
                responseStart["status"] :?> int
            else
                404

        and set (value: int) = responseStart["status"] <- value

    member x.Clear() =
        responseStart["status"] <- 200
        responseStart["headers"] <- ResizeArray<_>()
        responseBody["body"] <- [||]

    member x.WriteAsync(bytes: byte[]) = task {
        responseBody["body"] <- bytes

        if not x.HasStarted then
            do! send responseStart
            x.HasStarted <- true

        do! send responseBody
    }

    member x.SetHttpHeader(key: string, value: obj) =
        let headers = responseStart["headers"] :?> ResizeArray<string * obj>
        headers.Add((key, value.ToString()))

    member x.SetStatusCode(status: int) = responseStart["status"] <- status

    member x.Redirect(location: string, permanent: bool) =
        let statusCode = if permanent then 301 else 302

        x.SetStatusCode(statusCode)
        x.SetHttpHeader("Location", location)

type HttpContext(scope: Scope, receive: unit -> Task<Response>, send: Request -> Task<unit>) =
    // do printfn "Scope  %A" scope
    let scope = scope
    let send = send

    let items = Dictionary<string, obj>()

    let request = HttpRequest(scope, receive)
    let response = HttpResponse(send)

    member _.Items = items
    member _.Request = request
    member _.Response = response

    member _.RequestServices = scope["services"] :?> ServiceCollection

    member ctx.WriteBytesAsync(bytes: byte[]) = task {
        ctx.SetHttpHeader(HeaderNames.ContentLength, bytes.Length)

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
            |> Json.parseNativeAs<'T>
    }

    member inline x.GetService<'T>() : 'T =
        let (Singleton service) = x.RequestServices.GetService(typeof<'T>)
        service :?> 'T

    member x.ContinueWith(app: ASGIApp, next: HttpContext -> Task<unit>) = task {
        let mutable responseHasStarted = false

        let send' (request: Request) = task {
            if
                request.ContainsKey("type")
                && request["type"] :?> string = "http.response.start"
            then
                responseHasStarted <- true

            do! send request
        }

        do! app.Invoke(scope, receive, send')

        if not responseHasStarted then
            do! next x
    }
