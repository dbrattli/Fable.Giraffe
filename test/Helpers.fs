[<AutoOpen>]
module Fable.Giraffe.Tests.Helpers

open System
open System.Collections.Generic
open System.Threading.Tasks

open Fable.Giraffe
open System.Text

// ---------------------------------
// Common functions
// ---------------------------------


let waitForDebuggerToAttach() =
    printfn "Waiting for debugger to attach."
    printfn "Press enter when debugger is attached in order to continue test execution..."
    Console.ReadLine() |> ignore

let removeNewLines (html : string) : string =
    html.Replace(Environment.NewLine, String.Empty)


// ---------------------------------
// Test server/client setup
// ---------------------------------

let next : HttpFunc = Some >> Task.FromResult

let printBytes (bytes : byte[]) =
    bytes |> Array.fold (
        fun (s : string) (b : byte) ->
            match s.Length with
            | 0 -> $"%i{b}"
            | _ -> $"%s{s},%i{b}") ""

let getContentType (response : HttpResponse) =
    response.Headers["Content-Type"][0]

type HttpTestContext (scope: Scope, receive: unit -> Task<Response>, send: Request -> Task<unit>, body: ResizeArray<byte array>) =
    inherit HttpContext(scope, receive, send)

    member val buffer : ResizeArray<byte array> = body with get, set

    new (?method: string, ?path: string, ?status: int, ?headers: HeaderDictionary) =

        let _method = defaultArg method "GET"
        let _path = defaultArg path "/"
        let _status = defaultArg status 200
        let _headers = defaultArg headers (HeaderDictionary()) |> (fun x -> x.Scoped)
        let _scope = Dictionary<string, obj> (dict ["method", _method :> obj; "path", _path; "status", _status; "headers", _headers])
        let _response = Dictionary<string, obj> ()
        let _body = ResizeArray<byte array>()

        let send (response: Response) =
            let inline toMap kvps =
                kvps
                |> Seq.map (|KeyValue|)
                |> Map.ofSeq

            task {
                let xs = toMap response
                for KeyValue(key, value) in xs do
                    if key <> "type" then
                        _response.Add(key, value)

                    if key = "body" then
                        match value with
                        //| :? (byte array) as bytes -> // TODO: wait for upstream fix
                        | bytes ->
                            _body.Add (bytes :?> byte array)
                        | _ -> failwith "Body must be a byte array"
            }

        let receive () =
            task {
                return Dictionary<string, obj> ()
            }

        HttpTestContext(_scope, receive, send, _body)


    member this.Body
        with get () = this.buffer[0]
    member this.BodyAsText
        with get () = Encoding.UTF8.GetString(this.Body)
