namespace Fable.Giraffe

open System
open System.Collections.Generic
open System.Text
open System.Threading.Tasks

open Fable.Giraffe.Json


type Scope = Dictionary<string, obj>
type Request = Dictionary<string, obj>
type Response = Dictionary<string, obj>

type ReceiveAsync = unit -> Task<Response>
type SendAsync = Request -> Task<unit>

/// https://asgi.readthedocs.io/
type ASGIApp = Func<Scope, ReceiveAsync, SendAsync, Task<unit>>


type HttpRequest(scope: Scope, receive: ReceiveAsync) =
    member x.Path: string option = scope["path"] :?> string |> Some

    member x.Method: string = scope["method"] :?> string

    member x.Protocol: string = scope["type"] :?> string

    member x.GetTypedHeaders() : RequestHeaders =
        RequestHeaders(scope["headers"] :?> ResizeArray<ResizeArray<string>>)

    member x.GetBodyAsync() =
        task {
            let! response = receive ()
            return response["body"] :?> byte array
        }

    member x.Headers =
        // scope["headers"] is a ResizeArray<ResizeArray<string>> of [name; value]
        // pairs (same shape GetTypedHeaders reads) — NOT a Dictionary<string,string>.
        // Casting to Dictionary here used to throw at runtime.
        let raw = scope["headers"] :?> ResizeArray<ResizeArray<string>>
        let dict = Dictionary<string, string>()

        for pair in raw do
            if pair.Count >= 2 then
                dict[pair[0]] <- pair[1]

        HeaderDictionary(dict)

type HttpResponse(send: SendAsync) =
    let mutable statusCode = None

    // Build the ASGI event dicts directly. `Dictionary(dict [ ... ])` compiles to
    // make_dict(make_dict(...)) — two dict allocations each, per request — because the
    // inner `dict [ ... ]` is materialised only to be copied into the outer Dictionary.
    let responseStart =
        let d = Dictionary<string, obj>()
        d["type"] <- "http.response.start"
        d["headers"] <- ResizeArray<string * obj>()
        d

    let responseBody =
        let d = Dictionary<string, obj>()
        d["type"] <- "http.response.body"
        d

    member x.Headers =
        let tuples = responseStart["headers"] :?> ResizeArray<string * obj>
        let dict = Dictionary<string, string>()

        for (k, v) in tuples do
            dict[k] <- string v

        HeaderDictionary(dict)

    member val HasStarted: bool = false with get, set

    member x.StatusCode
        with get () =
            match statusCode with
            | Some statusCode -> statusCode
            | None -> 404

        and set (value: int) = responseStart["status"] <- toNativeInt value

    member x.Clear() =
        responseStart["headers"] <- ResizeArray<_>()
        responseBody["body"] <- [||]

    member x.WriteAsync(bytes: byte[]) =
        task {
            responseBody["body"] <- bytes

            if not x.HasStarted then
                match statusCode with
                | Some statusCode -> responseStart["status"] <- toNativeInt statusCode
                | None ->
                    responseStart["status"] <- toNativeInt 200
                    statusCode <- Some 200

                do! send responseStart
                x.HasStarted <- true

            do! send responseBody
        }

    member x.SetHttpHeader(key: string, value: obj) =
        let headers = responseStart["headers"] :?> ResizeArray<string * obj>
        headers.Add((key, value.ToString()))

    member x.SetStatusCode(status: int) = statusCode <- Some status

    member x.Redirect(location: string, permanent: bool) =
        let statusCode = if permanent then 301 else 302

        x.SetStatusCode(statusCode)
        x.SetHttpHeader("Location", location)

type HttpContext(scope: Scope, receive: ReceiveAsync, send: SendAsync) =
    let scope = scope
    let send = send

    let items = Dictionary<string, obj>()

    let request = HttpRequest(scope, receive)
    let response = HttpResponse(send)

    member _.Items = items
    member _.Request = request
    member _.Response = response

    member _.RequestServices = scope["services"] :?> ServiceCollection

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

    member x.ContinueWith(app: ASGIApp, next: HttpContext -> Task<unit>) =
        task {
            let mutable responseHasStarted = false

            let send' (request: Request) =
                task {
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
