module Fable.Giraffe.Tests.RemotingTests

open System.Text

open Fable.SimpleJson.Python

open Fable.Python.Tests.Util.Testing
open Fable.Giraffe


// ---------------------------------
// remoting Tests
// ---------------------------------

type Model = { Description: string; Count: int }

type IServer = {
    getNumbers : unit -> Async<int list>
    greet : string -> Async<string>
    updateModel: Model -> Async<Model>
    divide: float -> float -> Async<float>
    meaningOfLife: Async<int>
}

let greetingApi = {
    getNumbers = fun () -> async { return [1 .. 5] }
    greet = fun name ->
        async {
            let greeting = $"Hello, %s{name}"
            return greeting
        }
    updateModel = fun model ->
        async {
            return { model with Count = model.Count + 1 }
        }
    divide = fun x y -> async { return x / y }
    meaningOfLife = async { return 42 }
}
[<Fact>]
let ``test remoting: GET "/IServer/meaningOfLife" returns 42`` () =
    let testCtx = HttpTestContext(path="/IServer/meaningOfLife")
    let app =
        choose [
            Remoting.createApi()
            |> Remoting.fromValue greetingApi
            |> Remoting.buildHttpHandler
            setStatusCode 404 >=> text "Not found"
        ]
    let expected = "42" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx
        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some _ -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test remoting: GET "/IServer/getNumbers" returns numbers`` () =
    let testCtx = HttpTestContext(path="/IServer/getNumbers")
    let app =
        choose [
            Remoting.createApi()
            |> Remoting.fromValue greetingApi
            |> Remoting.buildHttpHandler
            setStatusCode 404 >=> text "Not found"
        ]
    let expected = "[1, 2, 3, 4, 5]" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx
        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some _ -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test remoting: POST "/greet" returns "Hello World"`` () =
    let testCtx = HttpTestContext(path="/IServer/greet", method="POST", body="""["World"]""")
    let app =
        Remoting.createApi()
        |> Remoting.fromValue greetingApi
        |> Remoting.buildHttpHandler

    let expected = "\"Hello, World\"" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx
        match result with
        | None -> failwith $"Result was expected to be {expected}"
        | Some _ -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test remoting: POST "/updateModel" returns updated model`` () =
    let model = { Description = "Test"; Count = 0 }
    let bytes = model |> List.singleton |> Json.serialize
    let testCtx = HttpTestContext(path="/IServer/updateModel", method="POST", body=bytes)
    let app =
        Remoting.createApi()
        |> Remoting.fromValue greetingApi
        |> Remoting.buildHttpHandler

    let expected = { model with Count = 1 } |> Json.serialize |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx
        match result with
        | None -> failwith $"Result was expected to be {expected}"
        | Some _ -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test remoting: POST "/divide" returns float`` () =
    let args = [ 5.0; 2.0 ]
    let bytes = args |> Json.serialize
    let testCtx = HttpTestContext(path="/IServer/divide", method="POST", body=bytes)
    let app =
        Remoting.createApi()
        |> Remoting.fromValue greetingApi
        |> Remoting.buildHttpHandler

    let expected = "2.5" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx
        match result with
        | None -> failwith $"Result was expected to be {expected}"
        | Some _ -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())
