// Learn more about F# at http://docs.microsoft.com/dotnet/fsharp
namespace Fable.Giraffe

open System.Text
open System.Threading.Tasks

open Fable.SimpleJson.Python

type HttpFuncResult = Task<HttpContext option>

type HttpFunc = HttpContext -> HttpFuncResult

type HttpHandler = HttpFunc -> HttpFunc


[<AutoOpen>]
module Core =
    let earlyReturn: HttpFunc = Some >> Task.FromResult
    let skipPipeline () : HttpFuncResult = Task.FromResult None

    let compose (handler1: HttpHandler) (handler2: HttpHandler) : HttpHandler =
        fun (final: HttpFunc) ->
            let func = final |> handler2 |> handler1

            fun (ctx: HttpContext) ->
                match ctx.Response.HasStarted with
                | true -> final ctx
                | false -> func ctx

    let (>=>) = compose

    /// <summary>
    /// The warbler function is a <see cref="HttpHandler"/> wrapper function which prevents a <see cref="HttpHandler"/> to be pre-evaluated at startup.
    /// </summary>
    /// <param name="f">A function which takes a HttpFunc * HttpContext tuple and returns a <see cref="HttpHandler"/> function.</param>
    /// <param name="next"></param>
    /// <param name="source"></param>
    /// <example>
    /// <code>
    /// warbler(fun _ -> someHttpHandler)
    /// </code>
    /// </example>
    /// <returns>Returns a <see cref="HttpHandler"/> function.</returns>
    let inline warbler f (source: HttpHandler) (next: HttpFunc) =
        fun (ctx: HttpContext) -> f (next, ctx) id next ctx
        |> source

    /// <summary>
    /// Iterates through a list of `HttpFunc` functions and returns the result of the first `HttpFunc` of which the outcome is `Some HttpContext`.
    /// </summary>
    /// <param name="funcs"></param>
    /// <param name="ctx"></param>
    /// <returns>A <see cref="HttpFuncResult"/>.</returns>
    let rec private chooseHttpFunc (funcs: HttpFunc list) : HttpFunc =
        fun (ctx: HttpContext) -> task {
            match funcs with
            | [] -> return None
            | func :: tail ->
                let! result = func ctx

                match result with
                | Some c -> return Some c
                | None -> return! chooseHttpFunc tail ctx
        }

    /// <summary>
    /// Iterates through a list of <see cref="HttpHandler"/> functions and returns the result of the first <see cref="HttpHandler"/> of which the outcome is Some HttpContext.
    /// Please mind that all <see cref="HttpHandler"/> functions will get pre-evaluated at runtime by applying the next (HttpFunc) parameter to each handler.
    /// </summary>
    /// <param name="handlers"></param>
    /// <param name="next"></param>
    /// <returns>A <see cref="HttpFunc"/>.</returns>
    let choose (handlers: HttpHandler list) : HttpHandler =
        fun (next: HttpFunc) ->
            let funcs = handlers |> List.map (fun h -> h next)
            fun (ctx: HttpContext) -> chooseHttpFunc funcs ctx

    /// <summary>
    /// Filters an incoming HTTP request based on the HTTP verb.
    /// </summary>
    /// <param name="validate">A validation function which checks for a single HTTP verb.</param>
    /// <param name="next"></param>
    /// <param name="ctx"></param>
    /// <returns>A Giraffe <see cref="HttpHandler"/> function which can be composed into a bigger web application.</returns>
    let private httpVerb (validate: string -> bool) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            if validate ctx.Request.Method then
                next ctx
            else
                skipPipeline ()

    let GET: HttpHandler = httpVerb HttpMethods.IsGet
    let POST: HttpHandler = httpVerb HttpMethods.IsPost
    let PUT: HttpHandler = httpVerb HttpMethods.IsPut
    let PATCH: HttpHandler = httpVerb HttpMethods.IsPatch
    let DELETE: HttpHandler = httpVerb HttpMethods.IsDelete
    let HEAD: HttpHandler = httpVerb HttpMethods.IsHead
    let OPTIONS: HttpHandler = httpVerb HttpMethods.IsOptions
    let TRACE: HttpHandler = httpVerb HttpMethods.IsTrace
    let CONNECT: HttpHandler = httpVerb HttpMethods.IsConnect

    let GET_HEAD: HttpHandler = choose [ GET; HEAD ]

    /// <summary>
    /// Clears the current <see cref="Microsoft.AspNetCore.Http.HttpResponse"/> object.
    /// This can be useful if a <see cref="HttpHandler"/> function needs to overwrite the response of all previous <see cref="HttpHandler"/> functions with its own response (most commonly used by an <see cref="ErrorHandler"/> function).
    /// </summary>
    /// <param name="next"></param>
    /// <param name="ctx"></param>
    /// <returns>A Giraffe <see cref="HttpHandler"/> function which can be composed into a bigger web application.</returns>
    let clearResponse: HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            ctx.Response.Clear()
            next ctx

    /// <summary>
    /// Sets the Content-Type HTTP header in the response.
    /// </summary>
    /// <param name="contentType">The mime type of the response (e.g.: application/json or text/html).</param>
    /// <param name="next"></param>
    /// <param name="ctx"></param>
    /// <returns>A Giraffe <see cref="HttpHandler"/> function which can be composed into a bigger web application.</returns>
    let setContentType contentType : HttpHandler =
        fun next ctx ->
            ctx.SetContentType contentType
            next ctx

    /// <summary>
    /// Sets the HTTP status code of the response.
    /// </summary>
    /// <param name="statusCode">The status code to be set in the response. For convenience you can use the static <see cref="Microsoft.AspNetCore.Http.StatusCodes"/> class for passing in named status codes instead of using pure int values.</param>
    /// <param name="next"></param>
    /// <param name="ctx"></param>
    /// <returns>A Giraffe <see cref="HttpHandler"/> function which can be composed into a bigger web application.</returns>
    let setStatusCode (statusCode: int) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            ctx.SetStatusCode statusCode
            next ctx

    /// <summary>
    /// Adds or sets a HTTP header in the response.
    /// </summary>
    /// <param name="key">The HTTP header name. For convenience you can use the static <see cref="Microsoft.Net.Http.Headers.HeaderNames"/> class for passing in strongly typed header names instead of using pure string values.</param>
    /// <param name="value">The value to be set. Non string values will be converted to a string using the object's ToString() method.</param>
    /// <param name="next"></param>
    /// <param name="ctx"></param>
    /// <returns>A Giraffe <see cref="HttpHandler"/> function which can be composed into a bigger web application.</returns>
    let setHttpHeader (key: string) (value: obj) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            ctx.SetHttpHeader(key, value)
            next ctx

    // <summary>
    /// Filters an incoming HTTP request based on the accepted mime types of the client (Accept HTTP header).
    /// If the client doesn't accept any of the provided mimeTypes then the handler will not continue executing the next <see cref="HttpHandler"/> function.
    /// </summary>
    /// <param name="mimeTypes">List of mime types of which the client has to accept at least one.</param>
    /// <param name="next"></param>
    /// <param name="ctx"></param>
    /// <returns>A Giraffe <see cref="HttpHandler"/> function which can be composed into a bigger web application.</returns>
    let mustAccept (mimeTypes: string list) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            let headers = ctx.Request.GetTypedHeaders()

            headers.Accept
            |> Seq.map (fun h -> h.ToString())
            |> Seq.exists (fun h -> mimeTypes |> Seq.contains h)
            |> function
                | true -> next ctx
                | false -> skipPipeline ()

    /// <summary>
    /// Redirects to a different location with a `302` or `301` (when permanent) HTTP status code.
    /// </summary>
    /// <param name="permanent">If true the redirect is permanent (301), otherwise temporary (302).</param>
    /// <param name="location">The URL to redirect the client to.</param>
    /// <param name="next"></param>
    /// <param name="ctx"></param>
    /// <returns>A Giraffe <see cref="HttpHandler"/> function which can be composed into a bigger web application.</returns>
    let redirectTo (permanent: bool) (location: string) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            ctx.Response.Redirect(location, permanent)
            Task.FromResult(Some ctx)

    // ---------------------------
    // Model binding functions
    // ---------------------------

    /// <summary>
    /// Parses a JSON payload into an instance of type 'T.
    /// </summary>
    /// <param name="f">A function which accepts an object of type 'T and returns a <see cref="HttpHandler"/> function.</param>
    /// <param name="next"></param>
    /// <param name="ctx"></param>
    /// <typeparam name="'T"></typeparam>
    /// <returns>A Giraffe <see cref="HttpHandler"/> function which can be composed into a bigger web application.</returns>
    let inline bindJson<'T> (f: 'T -> HttpHandler) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) -> task {
            let! model = ctx.BindJsonAsync<'T>()
            return! f model next ctx
        }

    /// <summary>
    /// Writes a byte array to the body of the HTTP response and sets the HTTP Content-Length header accordingly.
    /// </summary>
    /// <param name="bytes">The byte array to be send back to the client.</param>
    /// <param name="ctx"></param>
    /// <returns>A Giraffe <see cref="HttpHandler" /> function which can be composed into a bigger web application.</returns>
    let setBody (bytes: byte array) : HttpHandler =
        fun (_: HttpFunc) (ctx: HttpContext) -> ctx.WriteBytesAsync bytes

    // <summary>
    /// Writes an UTF-8 encoded string to the body of the HTTP response and sets the HTTP Content-Length header accordingly.
    /// </summary>
    /// <param name="str">The string value to be send back to the client.</param>
    /// <returns>A Giraffe <see cref="HttpHandler" /> function which can be composed into a bigger web application.</returns>
    let setBodyFromString (str: string) : HttpHandler =
        let bytes = Encoding.UTF8.GetBytes str
        fun (_: HttpFunc) (ctx: HttpContext) -> ctx.WriteBytesAsync bytes

    /// <summary>
    /// Writes an UTF-8 encoded string to the body of the HTTP response and sets the HTTP Content-Length header accordingly, as well as the Content-Type header to text/plain.
    /// </summary>
    /// <param name="str">The string value to be send back to the client.</param>
    /// <returns>A Giraffe <see cref="HttpHandler" /> function which can be composed into a bigger web application.</returns>
    let text (str: string) : HttpHandler =
        let bytes = Encoding.UTF8.GetBytes str

        fun (_: HttpFunc) (ctx: HttpContext) ->
            ctx.SetContentType "text/plain; charset=utf-8"
            ctx.WriteBytesAsync bytes

    /// <summary>
    /// Serializes an object to JSON and writes the output to the body of the HTTP response.
    /// It also sets the HTTP Content-Type header to application/json and sets the Content-Length header accordingly.
    /// </summary>
    /// <param name="dataObj">The object to be send back to the client.</param>
    /// <param name="ctx"></param>
    /// <typeparam name="'T"></typeparam>
    /// <returns>A Giraffe <see cref="HttpHandler" /> function which can be composed into a bigger web application.</returns>
    let inline json<'T> (dataObj: 'T) : HttpHandler =
        fun (_: HttpFunc) (ctx: HttpContext) ->
            let json = Json.serialize dataObj
            let bytes = Encoding.UTF8.GetBytes json
            ctx.SetContentType "application/json; charset=utf-8"
            ctx.WriteBytesAsync bytes
