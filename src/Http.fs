namespace Fable.Giraffe

open System
open System.Collections.Generic

// Platform-agnostic HTTP header/method helper types shared by every backend
// (Python/BEAM/JS). These touch no platform types — only System.Collections.Generic
// and StringValues (from Helpers.fs) — so they live here and are linked by each
// target's fsproj rather than copy-pasted into every HttpContext.fs.

module HeaderNames =
    [<Literal>]
    let ContentType = "content-type"

    [<Literal>]
    let ContentLength = "content-length"

module HttpMethods =
    [<Literal>]
    let Head = "HEAD"

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
            |> Seq.map (fun (KeyValue(k, v)) -> (k, StringValues v))
            |> dict

        HeaderDictionary(Dictionary(dict))

    new() = HeaderDictionary(Dictionary<string, StringValues>())

    member x.Item(key: string) = headers[key.ToLower()]

    member x.Add(key: string, value: string) =
        headers[key.ToLower()] <- StringValues(value)

    member x.Add(key: string, value: StringValues) = headers[key.ToLower()] <- value

    member x.Scoped =
        headers
        |> Seq.map (fun (KeyValue(k, v)) -> ResizeArray([ k; String.Join(", ", v.ToArray()) ]))
        |> ResizeArray


type StringSegment(value: string) =
    member x.Value = value

    override x.ToString() = value

    static member Empty = StringSegment("")

[<AllowNullLiteral>]
type MediaTypeHeaderValue(value: string) =
    let parts = value.Split(';')
    let mediaType = parts[0].Trim()

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
                |> Seq.tryFind (fun x -> x[0].ToLower() = "accept")

            match found with
            | Some value ->
                value
                |> Seq.skip 1
                |> Seq.map MediaTypeHeaderValue
                |> ResizeArray
            | _ -> ResizeArray<MediaTypeHeaderValue>()

        and set (_value: ResizeArray<MediaTypeHeaderValue>) = failwith "Not implemented"
