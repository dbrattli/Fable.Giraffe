module Fable.Giraffe.Tests.RemotingTests

open System
open System.Text

open Fable.Giraffe
open Fable.Python.Tests.Util.Testing

// ---------------------------------
// remoting Tests
// ---------------------------------

type IServer = {
    getNumbers : unit -> Async<int list>
    greet : string -> Async<string>
}

let greetingApi = {
    getNumbers = fun () -> async { return [1 .. 5] }
    greet = fun name ->
        async {
            let greeting = $"Hello, %s{name}"
            return greeting
        }
}

[<Fact>]
let ``test remoting: GET "/IServer/getNumbers" returns numbers`` () =
    let testCtx = HttpTestContext(path="/IServer/getNumbers")
    let app =
        choose [
            Remoting.createApi()
            |> Remoting.fromValue greetingApi
            setStatusCode 404 >=> text "Not found"
        ]
    let expected = "[1, 2, 3, 4, 5]" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx
        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some _ -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

// [<Fact>]
// let ``test remoting: GET "/greet" returns "Hello World"`` () =
//     let testCtx = HttpTestContext(path="/")
//     let app =
//         Remoting.createApi()
//         |> Remoting.fromValue greetingApi
//
//     let expected = "Hello World" |> Encoding.UTF8.GetBytes
//     failwith ""
//
//     task {
//         let! result = app next testCtx
//         match result with
//         | None     -> failwith $"Result was expected to be {expected}"
//         | Some _ -> testCtx.Body |> equal expected
//     } |> (fun tsk -> tsk.RunSynchronously)
