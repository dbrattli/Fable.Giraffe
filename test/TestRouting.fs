module Giraffe.Tests.RoutingTests

open System
open System.Text

open Fable.Giraffe
open Fable.Python.Tests.Util.Testing

// ---------------------------------
// route Tests
// ---------------------------------

[<Fact>]
let ``test route: GET "/" returns "Hello World"`` () =
    let testCtx = HttpTestContext(path="/")
    let app =
        GET >=> choose [
            route "/"    >=> text "Hello World"
            route "/foo" >=> text "bar"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "Hello World" |> Encoding.UTF8.GetBytes

    let tsk =
            task {
            let! result = app next testCtx

            match result with
            | None     -> failwith $"Result was expected to be {expected}"
            | Some _ -> testCtx.Body |> equal expected
        }

    tsk.GetAwaiter().GetResult()

[<Fact>]
let ``test route: GET "/foo" returns "bar"`` () =
    let testCtx = HttpTestContext(path="/foo")

    let app =
        GET >=> choose [
            route "/"    >=> text "Hello World"
            route "/foo" >=> text "bar"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "bar" |> Encoding.UTF8.GetBytes

    let tsk =
        task {
            let! result = app next testCtx

            match result with
            | None     -> failwith $"Result was expected to be {expected}"
            | Some _ -> testCtx.Body |> equal expected
        }
    tsk.GetAwaiter().GetResult()

[<Fact>]
let ``test route: GET "/FOO" returns 404 "Not found"`` () =
    let testCtx = HttpTestContext(path="/FOO")
    let app =
        GET >=> choose [
            route "/"    >=> text "Hello World"
            route "/foo" >=> text "bar"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "Not found" |> Encoding.UTF8.GetBytes

    let tsk =
        task {
            let! result = app next testCtx

            match result with
            | None     -> failwith $"Result was expected to be {expected}"
            | Some ctx ->
                let body = testCtx.Body
                body |> equal expected
                ctx.Response.StatusCode |> equal 404
        }
    tsk.GetAwaiter().GetResult()

// ---------------------------------
// routeCi Tests
// ---------------------------------

[<Fact>]
let ``test GET "/JSON" returns "BaR"`` () =
    let testCtx = HttpTestContext(path="/JSON")
    let app =
        GET >=> choose [
            route   "/"       >=> text "Hello World"
            route   "/foo"    >=> text "bar"
            route   "/json"   >=> text "FOO"
            routeCi "/json"   >=> text "BaR"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "BaR" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())

// ---------------------------------
// routex Tests
// ---------------------------------

[<Fact>]
let ``test routex: GET "/" returns "Hello World"`` () =
    let testCtx = HttpTestContext(path="/")
    let app =
        GET >=> choose [
            routex "/"    >=> text "Hello World"
            routex "/foo" >=> text "bar"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "Hello World" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())

[<Fact>]
let ``test routex: GET "/foo" returns "bar"`` () =
    let testCtx = HttpTestContext(path="/foo")
    let app =
        GET >=> choose [
            routex "/"    >=> text "Hello World"
            routex "/foo" >=> text "bar"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "bar" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())

[<Fact>]
let ``test routex: GET "/FOO" returns 404 "Not found"`` () =
    let testCtx = HttpTestContext(path="/FOO")
    let app =
        GET >=> choose [
            routex "/"    >=> text "Hello World"
            routex "/foo" >=> text "bar"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "Not found" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx ->
            let body = testCtx.Body
            body |> equal expected
            ctx.Response.StatusCode |> equal 404
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())

[<Fact>]
let ``test routex: GET "/foo///" returns "bar"`` () =
    let testCtx = HttpTestContext(path="/foo///")
    let app =
        GET >=> choose [
            routex "/"        >=> text "Hello World"
            routex "/foo(/*)" >=> text "bar"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "bar" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())

[<Fact>]
let ``test routex: GET "/foo2" returns "bar"`` () =
    let testCtx = HttpTestContext(path="/foo2")
    let app =
        GET >=> choose [
            routex "/"         >=> text "Hello World"
            routex "/foo2(/*)" >=> text "bar"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "bar" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     ->failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())

// ---------------------------------
// routeCix Tests
// ---------------------------------

[<Fact>]
let ``test routeCix: GET "/CaSe///" returns "right"`` () =
    let testCtx = HttpTestContext(path="/CaSe///")
    let app =
        GET >=> choose [
            routex   "/case(/*)" >=> text "wrong"
            routeCix "/case(/*)" >=> text "right"
            setStatusCode 404    >=> text "Not found" ]

    let expected = "right" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx
        match result with
        | None     ->failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())

//
// ---------------------------------
// routef Tests
// ---------------------------------

[<Fact>]
let ``test routef: GET "/foo/blah blah/bar" returns "blah blah"`` () =
    let testCtx = HttpTestContext(path="/foo/blah blah/bar")
    let app =
        GET >=> choose [
            route   "/"       >=> text "Hello World"
            route   "/foo"    >=> text "bar"
            routef "/foo/%s/bar" text
            routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
            setStatusCode 404 >=> text "Not found" ]

    let expected = "blah blah" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     ->failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())

[<Fact>]
let ``test routef: GET "/foo/johndoe/59" returns "Name: johndoe, Age: 59"`` () =
    let testCtx = HttpTestContext(path="/foo/johndoe/59")
    let app =
        GET >=> choose [
            route   "/"       >=> text "Hello World"
            route   "/foo"    >=> text "bar"
            routef "/foo/%s/bar" text
            routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
            setStatusCode 404 >=> text "Not found" ]

    let expected = "Name: johndoe, Age: 59" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     ->failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())

[<Fact>]
let ``test routef: GET "/foo/b%2Fc/bar" returns "b%2Fc"`` () =
    let testCtx = HttpTestContext(path="/foo/b%2Fc/bar")
    let app =
        GET >=> choose [
            route  "/"       >=> text "Hello World"
            route  "/foo"    >=> text "bar"
            routef "/foo/%s/bar" text
            routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
            setStatusCode 404 >=> text "Not found" ]

    let expected = "b/c" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     ->failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())

[<Fact>]
let ``test routef: GET "/foo/a%2Fb%2Bc.d%2Ce/bar" returns "a%2Fb%2Bc.d%2Ce"`` () =
    let testCtx = HttpTestContext(path="/foo/a%2Fb%2Bc.d%2Ce/bar")
    let app =
        GET >=> choose [
            route  "/"       >=> text "Hello World"
            route  "/foo"    >=> text "bar"
            routef "/foo/%s/bar" text
            routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
            setStatusCode 404 >=> text "Not found" ]

    let expected = "a/b%2Bc.d%2Ce" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     ->failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())
//
// [<Theory>]
// [<InlineData( "/API/hello/", "routeStartsWithf:hello" )>]
// [<InlineData( "/API/hello/more", "routeStartsWithf:hello" )>]
// [<InlineData( "/aPi/hello/", "routeStartsWithCif:hello" )>]
// [<InlineData( "/APi/hello/more/", "routeStartsWithCif:hello" )>]
// [<InlineData( "/aPI/hello/more", "routeStartsWithCif:hello" )>]
// [<InlineData( "/test/hello/more", "Not found" )>]
// [<InlineData( "/TEST/hello/more", "Not found" )>]
// let ``routeStartsWith(f|Cif)`` (uri:string, expected:string) =
//
//     let app =
//         GET >=> choose [
//             routeStartsWithf "/API/%s/" (fun capture -> text ("routeStartsWithf:" + capture))
//             routeStartsWithCif "/api/%s/" (fun capture -> text ("routeStartsWithCif:" + capture))
//             setStatusCode 404 >=> text "Not found"
//         ]
//
//     let ctx = Substitute.For<HttpContext>()
//     ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
//     ctx.Request.Path.ReturnsForAnyArgs (PathString(uri)) |> ignore
//     ctx.Response.Body <- new MemoryStream()
//
//     task {
//         let! result = app next ctx
//
//         match result with
//         | None     -> assertFailf "Result was expected to be %s"  expected
//         | Some ctx -> Assert.Equal(expected, getBody ctx)
//     }
//
[<Fact>]
let ``test routef: GET "/foo/%O/bar/%O" returns "Guid1: ..., Guid2: ..."`` () =
    let testCtx = HttpTestContext(path="/foo/4ec87f064d1e41b49342ab1aead1f99d/bar/2a6c9185-95d9-4d8c-80a6-575f99c2a716")
    let app =
        GET >=> choose [
            route  "/"       >=> text "Hello World"
            route  "/foo"    >=> text "bar"
            routef "/foo/%s/bar" text
            routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
            routef "/foo/%O/bar/%O" (fun (guid1 : Guid, guid2 : Guid) -> text (sprintf "Guid1: %O, Guid2: %O" guid1 guid2))
            setStatusCode 404 >=> text "Not found" ]

    let expected = "Guid1: 4ec87f06-4d1e-41b4-9342-ab1aead1f99d, Guid2: 2a6c9185-95d9-4d8c-80a6-575f99c2a716" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     ->failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())

[<Fact>]
let ``test routef: GET "/foo/%u/bar/%u" returns "Id1: ..., Id2: ..."`` () =
    let testCtx = HttpTestContext(path="/foo/r1iKapqh_s4/bar/5aLu720NzTs")
    let app =
        GET >=> choose [
            route  "/"       >=> text "Hello World"
            route  "/foo"    >=> text "bar"
            routef "/foo/%s/bar" text
            routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
            routef "/foo/%u/bar/%u" (fun (id1 : uint64, id2 : uint64) -> text (sprintf "Id1: %u, Id2: %u" id1 id2))
            setStatusCode 404 >=> text "Not found" ]

    let expected = "Id1: 12635000945053400782, Id2: 16547050693006839099" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())

[<Fact>]
let ``test routef: GET "/foo/bar/baz/qux" returns 404 "Not found"`` () =
    let testCtx = HttpTestContext(path="/foo/bar/baz/qux")
    let app =
        GET >=> choose [
            routef "/foo/%s/%s" (fun (s1, s2) -> text (sprintf "%s,%s" s1 s2))
            setStatusCode 404 >=> text "Not found" ]

    let expected = "Not found" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx ->
            let body = testCtx.Body
            body |> equal expected
            ctx.Response.StatusCode |> equal 404
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())

// ---------------------------------
// routeCif Tests
// ---------------------------------

[<Fact>]
let ``test POST "/POsT/1" returns "1"`` () =
    let testCtx = HttpTestContext(path="/POsT/1", method="POST")
    let app =
        choose [
            GET >=> choose [
                route "/" >=> text "Hello World" ]
            POST >=> choose [
                route    "/post/1" >=> text "2"
                routeCif "/post/%i" json ]
            setStatusCode 404 >=> text "Not found" ]

    let expected = "1" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())

[<Fact>]
let ``test POST "/POsT/523" returns "523"`` () =
    let testCtx = HttpTestContext(path="/POsT/523", method="POST")
    let app =
        choose [
            GET >=> choose [
                route "/" >=> text "Hello World" ]
            POST >=> choose [
                route    "/post/1" >=> text "1"
                routeCif "/post/%i" json ]
            setStatusCode 404 >=> text "Not found" ]

    let expected = "523" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.GetAwaiter().GetResult())

// ---------------------------------
// routeBind Tests
// ---------------------------------

[<CLIMutable>]
type RouteBind   = { Foo : string; Bar : int; Id : Guid }

[<CLIMutable>]
type RouteBindId = { Id : Guid }

type PaymentMethod =
    | Credit
    | Debit

[<CLIMutable>]
type Purchase = { PaymentMethod : PaymentMethod }

// [<Fact>]
// let ``test routeBind: Route has matching union type``() =
//     let test = HttpTester(path="/credit")
//     let app =
//         GET >=> choose [
//             routeBind<Purchase> "/{paymentMethod}"
//                 (fun p -> sprintf "%s" (p.PaymentMethod.ToString()) |> text)
//             setStatusCode 404 >=> text "Not found" ]
//
//     let expected = "Credit"
//     task {
//         let! result = app next test.Context
//
//         match result with
//         | None     -> assertFailf "Result was expected to be %s" expected
//         | Some ctx -> Assert.Equal(expected, getBody ctx)
//     }
//
// [<Fact>]
// let ``routeBind: Route doesn't match union type``() =
//     let ctx = Substitute.For<HttpContext>()
//     let app =
//         GET >=> choose [
//             routeBind<Purchase> "/{paymentMethod}"
//                 (fun p -> sprintf "%s" (p.PaymentMethod.ToString()) |> text)
//             setStatusCode 404 >=> text "Not found" ]
//     ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
//     ctx.Request.Path.ReturnsForAnyArgs (PathString("/wrong")) |> ignore
//     ctx.Response.Body <- new MemoryStream()
//     let expected = "Not found"
//     task {
//         let! result = app next ctx
//
//         match result with
//         | None     -> assertFailf "Result was expected to be %s" expected
//         | Some ctx -> Assert.Equal(expected, getBody ctx)
//     }
//
// [<Fact>]
// let ``routeBind: Normal route``() =
//     let ctx = Substitute.For<HttpContext>()
//     let app = GET >=> routeBind<RouteBind> "/{foo}/{bar}/{id}" (fun m -> sprintf "%s %i %O" m.Foo m.Bar m.Id |> text)
//     ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
//     ctx.Request.Path.ReturnsForAnyArgs (PathString("/Hello/1/f40580b1-d55b-4fe2-b6fb-ca4f90749a9d")) |> ignore
//     ctx.Response.Body <- new MemoryStream()
//     task {
//         let! result = app next ctx
//
//         match result with
//         | None     -> assertFail "It was expected that the result would be Hello 1"
//         | Some ctx ->
//             let body = getBody ctx
//             Assert.Equal("Hello 1 f40580b1-d55b-4fe2-b6fb-ca4f90749a9d", body)
//     }
//
// [<Fact>]
// let ``routeBind: Normal route with trailing slash``() =
//     let ctx = Substitute.For<HttpContext>()
//     let app = GET >=> routeBind<RouteBind> "/{foo}/{bar}/{id}/" (fun m -> sprintf "%s %i %O" m.Foo m.Bar m.Id |> text)
//     ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
//     ctx.Request.Path.ReturnsForAnyArgs (PathString("/Hello/1/f40580b1-d55b-4fe2-b6fb-ca4f90749a9d/")) |> ignore
//     ctx.Response.Body <- new MemoryStream()
//     task {
//         let! result = app next ctx
//
//         match result with
//         | None     -> assertFail "It was expected that the result would be Hello 1f40580b1-d55b-4fe2-b6fb-ca4f90749a9d"
//         | Some ctx ->
//             let body = getBody ctx
//             Assert.Equal("Hello 1 f40580b1-d55b-4fe2-b6fb-ca4f90749a9d", body)
//     }
//
// [<Fact>]
// let ``routeBind: Route with (/*) matches mutliple trailing slashes``() =
//     let ctx = Substitute.For<HttpContext>()
//     let app = GET >=> routeBind<RouteBind> "/{foo}/{bar}/{id}(/*)" (fun m -> sprintf "%s %i %O" m.Foo m.Bar m.Id |> text)
//     ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
//     ctx.Request.Path.ReturnsForAnyArgs (PathString("/Hello/1/f40580b1-d55b-4fe2-b6fb-ca4f90749a9d///")) |> ignore
//     ctx.Response.Body <- new MemoryStream()
//     task {
//         let! result = app next ctx
//
//         match result with
//         | None     -> assertFail "It was expected that the result would be Hello 1f40580b1-d55b-4fe2-b6fb-ca4f90749a9d"
//         | Some ctx ->
//             let body = getBody ctx
//             Assert.Equal("Hello 1 f40580b1-d55b-4fe2-b6fb-ca4f90749a9d", body)
//     }
//
// [<Fact>]
// let ``routeBind: Route with (/*) matches no trailing slash``() =
//     let ctx = Substitute.For<HttpContext>()
//     let app = GET >=> routeBind<RouteBind> "/{foo}/{bar}/{id}(/*)" (fun m -> sprintf "%s %i %O" m.Foo m.Bar m.Id |> text)
//     ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
//     ctx.Request.Path.ReturnsForAnyArgs (PathString("/Hello/2/f40580b1-d55b-4fe2-b6fb-ca4f90749a9d")) |> ignore
//     ctx.Response.Body <- new MemoryStream()
//     task {
//         let! result = app next ctx
//
//         match result with
//         | None     -> assertFail "It was expected that the result would be Hello 1f40580b1-d55b-4fe2-b6fb-ca4f90749a9d"
//         | Some ctx ->
//             let body = getBody ctx
//             Assert.Equal("Hello 2 f40580b1-d55b-4fe2-b6fb-ca4f90749a9d", body)
//     }
//
// [<Fact>]
// let ``routeBind: Route with (/?) matches single trailing slash``() =
//     let ctx = Substitute.For<HttpContext>()
//     let app = GET >=> routeBind<RouteBind> "/{foo}/{bar}/{id}(/?)" (fun m -> sprintf "%s %i %O" m.Foo m.Bar m.Id |> text)
//     ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
//     ctx.Request.Path.ReturnsForAnyArgs (PathString("/Hello/3/f40580b1-d55b-4fe2-b6fb-ca4f90749a9d/")) |> ignore
//     ctx.Response.Body <- new MemoryStream()
//     task {
//         let! result = app next ctx
//
//         match result with
//         | None     -> assertFail "It was expected that the result would be Hello 3 1f40580b1-d55b-4fe2-b6fb-ca4f90749a9d"
//         | Some ctx ->
//             let body = getBody ctx
//             Assert.Equal("Hello 3 f40580b1-d55b-4fe2-b6fb-ca4f90749a9d", body)
//     }
//
// [<Fact>]
// let ``routeBind: Route with (/?) matches no trailing slash``() =
//     let ctx = Substitute.For<HttpContext>()
//     let app = GET >=> routeBind<RouteBind> "/{foo}/{bar}/{id}(/?)" (fun m -> sprintf "%s %i %O" m.Foo m.Bar m.Id |> text)
//     ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
//     ctx.Request.Path.ReturnsForAnyArgs (PathString("/Hello/4/f40580b1-d55b-4fe2-b6fb-ca4f90749a9d")) |> ignore
//     ctx.Response.Body <- new MemoryStream()
//     task {
//         let! result = app next ctx
//
//         match result with
//         | None     -> assertFail "It was expected that the result would be Hello 4 1f40580b1-d55b-4fe2-b6fb-ca4f90749a9d"
//         | Some ctx ->
//             let body = getBody ctx
//             Assert.Equal("Hello 4 f40580b1-d55b-4fe2-b6fb-ca4f90749a9d", body)
//     }
//
// [<Fact>]
// let ``routeBind: Route with non parameterised part``() =
//     let ctx = Substitute.For<HttpContext>()
//     let app = GET >=> routeBind<RouteBind> "/api/{foo}/{bar}/{id}" (fun m -> sprintf "%s %i %O" m.Foo m.Bar m.Id |> text)
//     ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
//     ctx.Request.Path.ReturnsForAnyArgs (PathString("/api/Hello/1/f40580b1-d55b-4fe2-b6fb-ca4f90749a9d")) |> ignore
//     ctx.Response.Body <- new MemoryStream()
//     task {
//         let! result = app next ctx
//
//         match result with
//         | None     -> assertFail "It was expected that the result would be Hello 1"
//         | Some ctx ->
//             let body = getBody ctx
//             Assert.Equal("Hello 1 f40580b1-d55b-4fe2-b6fb-ca4f90749a9d", body)
//     }
//
// [<Fact>]
// let ``routeBind: Route with non parameterised part and with Guid binding``() =
//     let ctx = Substitute.For<HttpContext>()
//     let app = GET >=> routeBind<RouteBindId> "/api/{id}" (fun m -> sprintf "%O" m.Id |> text)
//     ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
//     ctx.Request.Path.ReturnsForAnyArgs (PathString("/api/f40580b1-d55b-4fe2-b6fb-ca4f90749a9d")) |> ignore
//     ctx.Response.Body <- new MemoryStream()
//     task {
//         let! result = app next ctx
//
//         match result with
//         | None     -> assertFail "It was expected that the result would be f40580b1-d55b-4fe2-b6fb-ca4f90749a9d"
//         | Some ctx ->
//             let body = getBody ctx
//             Assert.Equal("f40580b1-d55b-4fe2-b6fb-ca4f90749a9d", body)
//     }
//
// [<Fact>]
// let ``routeBind: Route with non parameterised part and with (/?)``() =
//     let ctx = Substitute.For<HttpContext>()
//     let app = GET >=> routeBind<RouteBindId> "/api/{id}(/?)" (fun m -> sprintf "%O" m.Id |> text)
//     ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
//     ctx.Request.Path.ReturnsForAnyArgs (PathString("/api/f40580b1-d55b-4fe2-b6fb-ca4f90749a9d/")) |> ignore
//     ctx.Response.Body <- new MemoryStream()
//     task {
//         let! result = app next ctx
//
//         match result with
//         | None     -> assertFail "It was expected that the result would be f40580b1-d55b-4fe2-b6fb-ca4f90749a9d"
//         | Some ctx ->
//             let body = getBody ctx
//             Assert.Equal("f40580b1-d55b-4fe2-b6fb-ca4f90749a9d", body)
//     }
//
// [<Fact>]
// let ``routeBind: Route nested after subRoute``() =
//     let ctx = Substitute.For<HttpContext>()
//     let app = GET >=> subRoute "/test" (routeBind<RouteBindId> "/api/{id}(/?)" (fun m -> sprintf "%O" m.Id |> text))
//     ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
//     ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
//     ctx.Request.Path.ReturnsForAnyArgs (PathString("/test/api/f40580b1-d55b-4fe2-b6fb-ca4f90749a9d/")) |> ignore
//     ctx.Response.Body <- new MemoryStream()
//     task {
//         let! result = app next ctx
//
//         match result with
//         | None     -> assertFail "It was expected that the result would be f40580b1-d55b-4fe2-b6fb-ca4f90749a9d"
//         | Some ctx ->
//             let body = getBody ctx
//             Assert.Equal("f40580b1-d55b-4fe2-b6fb-ca4f90749a9d", body)
//     }
//
// // ---------------------------------
// // subRoute Tests
// // ---------------------------------
//
[<Fact>]
let ``test subRoute: Route with empty route`` () =
    let testCtx = HttpTestContext(path="/api", method="GET")
    let app =
        GET >=> choose [
            route "/"    >=> text "Hello World"
            route "/foo" >=> text "bar"
            subRoute "/api" (
                choose [
                    route ""       >=> text "api root"
                    route "/admin" >=> text "admin"
                    route "/users" >=> text "users" ] )
            route "/api/test" >=> text "test"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "api root" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test subRoute: Normal nested route after subRoute`` () =
    let testCtx = HttpTestContext(path="/api/users", method="GET")
    let app =
        GET >=> choose [
            route "/"    >=> text "Hello World"
            route "/foo" >=> text "bar"
            subRoute "/api" (
                choose [
                    route ""       >=> text "api root"
                    route "/admin" >=> text "admin"
                    route "/users" >=> text "users" ] )
            route "/api/test" >=> text "test"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "users" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test subRoute: Route after subRoute has same beginning of path`` () =

    task {
        let testCtx = HttpTestContext(path="/api/test", method="GET")

        let app =
            GET >=> choose [
                route "/"    >=> text "Hello World"
                route "/foo" >=> text "bar"
                subRoute "/api" (
                    choose [
                        route ""       >=> text "api root"
                        route "/admin" >=> text "admin"
                        route "/users" >=> text "users" ] )
                route "/api/test" >=> text "test"
                setStatusCode 404 >=> text "Not found" ]

        let expected = "test" |> Encoding.UTF8.GetBytes

        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test subRoute: Nested sub routes`` () =
    let testCtx = HttpTestContext(path="/api/v2/users", method="GET")
    let app =
        GET >=> choose [
            route "/"    >=> text "Hello World"
            route "/foo" >=> text "bar"
            subRoute "/api" (
                choose [
                    route ""       >=> text "api root"
                    route "/admin" >=> text "admin"
                    route "/users" >=> text "users"
                    subRoute "/v2" (
                        choose [
                            route ""       >=> text "api root v2"
                            route "/admin" >=> text "admin v2"
                            route "/users" >=> text "users v2"
                        ]
                    )
                ]
            )
            route "/api/test" >=> text "test"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "users v2" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test subRoute: Multiple nested sub routes`` () =
    let testCtx = HttpTestContext(path="/api/v2/admin2", method="GET")
    let app =
        GET >=> choose [
            route "/"    >=> text "Hello World"
            route "/foo" >=> text "bar"
            subRoute "/api" (
                choose [
                    route "/users" >=> text "users"
                    subRoute "/v2" (
                        choose [
                            route "/admin" >=> text "admin v2"
                            route "/users" >=> text "users v2"
                        ]
                    )
                    subRoute "/v2" (
                        route "/admin2" >=> text "correct admin2"
                    )
                ]
            )
            route "/api/test"   >=> text "test"
            route "/api/v2/else" >=> text "else"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "correct admin2" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test subRoute: Route after nested sub routes has same beginning of path`` () =
    let testCtx = HttpTestContext(path="/api/v2/else", method="GET")
    let app =
        GET >=> choose [
            route "/"    >=> text "Hello World"
            route "/foo" >=> text "bar"
            subRoute "/api" (
                choose [
                    route ""       >=> text "api root"
                    route "/admin" >=> text "admin"
                    route "/users" >=> text "users"
                    subRoute "/v2" (
                        choose [
                            route ""       >=> text "api root v2"
                            route "/admin" >=> text "admin v2"
                            route "/users" >=> text "users v2"
                        ]
                    )
                    route "/yada" >=> text "yada"
                ]
            )
            route "/api/test"   >=> text "test"
            route "/api/v2/else" >=> text "else"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "else" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test subRoute: routef inside subRoute`` () =
    let testCtx = HttpTestContext(path="/api/foo/bar/yadayada", method="GET")
    let app =
        GET >=> choose [
            route "/"    >=> text "Hello World"
            route "/foo" >=> text "bar"
            subRoute "/api" (
                choose [
                    route  "" >=> text "api root"
                    routef "/foo/bar/%s" text ] )
            route "/api/test" >=> text "test"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "yadayada" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

// // ---------------------------------
// // subRoutef Tests
// // ---------------------------------
//
// [<Fact>]
// let ``routef: Validation`` () =
//     Assert.Throws( fun () ->
//         GET >=> choose [
//             route   "/"       >=> text "Hello World"
//             route   "/foo"    >=> text "bar"
//             routef "/foo/%s/%d" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
//             setStatusCode 404 >=> text "Not found" ]
//         |> ignore
//     ) |> ignore
//
[<Fact>]
let ``test subRoutef: GET "/" returns "Not found"`` () =
    let testCtx = HttpTestContext(path="/", method="GET")
    let app =
        GET >=> choose [
            subRoutef "/%s/%i" (fun (lang, version) ->
                choose [
                    route  "/foo" >=> text "bar"
                    routef "/%s" (fun name -> text (sprintf "Hello %s! Lang: %s, Version: %i" name lang version))
                ])
            route "/bar" >=> text "foo"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "Not found" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test subRoutef: GET "/bar" returns "foo"`` () =
    let testCtx = HttpTestContext(path="/bar", method="GET")
    let app =
        GET >=> choose [
            subRoutef "/%s/%i" (fun (lang, version) ->
                choose [
                    route  "/foo" >=> text "bar"
                    routef "/%s" (fun name -> text (sprintf "Hello %s! Lang: %s, Version: %i" name lang version))
                ])
            route "/bar" >=> text "foo"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "foo" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test subRoutef: GET "/John/5/foo" returns "bar"`` () =
    let testCtx = HttpTestContext(path="/John/5/foo", method="GET")
    let app =
        GET >=> choose [
            subRoutef "/%s/%i" (fun (lang, version) ->
                choose [
                    route  "/foo" >=> text "bar"
                    routef "/%s" (fun name -> text (sprintf "Hello %s! Lang: %s, Version: %i" name lang version))
                ])
            route "/bar" >=> text "foo"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "bar" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test subRoutef: GET "/en/10/Julia" returns "Hello Julia! Lang: en, Version: 10"`` () =
    let testCtx = HttpTestContext(path="/en/10/Julia", method="GET")
    let app =
        GET >=> choose [
            subRoutef "/%s/%i" (fun (lang, version) ->
                choose [
                    route  "/foo" >=> text "bar"
                    routef "/%s" (fun name -> text (sprintf "Hello %s! Lang: %s, Version: %i" name lang version))
                ])
            route "/bar" >=> text "foo"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "Hello Julia! Lang: en, Version: 10" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test subRoutef: GET "/en/10/api/Julia" returns "Hello Julia! Lang: en, Version: 10"`` () =
    let testCtx = HttpTestContext(path="/en/10/api/Julia", method="GET")
    let app =
        GET >=> choose [
            subRoutef "/%s/%i/api" (fun (lang, version) ->
                choose [
                    route  "/foo" >=> text "bar"
                    routef "/%s" (fun name -> text (sprintf "Hello %s! Lang: %s, Version: %i" name lang version))
                ])
            route "/bar" >=> text "foo"
            setStatusCode 404 >=> text "Not found" ]

    let expected = "Hello Julia! Lang: en, Version: 10" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

// ---------------------------------
// subRouteCi Tests
// ---------------------------------

[<Fact>]
let ``test subRouteCi: Non-filtering handler after subRouteCi is called`` () =
    let testCtx = HttpTestContext(path="/FOO", method="GET")
    let app =
        GET >=> choose [
            subRouteCi "/foo" (text "subroute /foo")
            setStatusCode 404 >=> text "Not found" ]

    let expected = "subroute /foo" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test subRouteCi: Nested route after subRouteCi is called`` () =
    let testCtx = HttpTestContext(path="/FOO/bar", method="GET")
    let app =
        GET >=> choose [
            subRouteCi "/foo" (
                route "/bar" >=> text "subroute /foo/bar")
            setStatusCode 404 >=> text "Not found" ]

    let expected = "subroute /foo/bar" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test subRouteCi: Nested route after subRouteCi is still case sensitive`` () =
    let testCtx = HttpTestContext(path="/FOO/BAR", method="GET")
    let app =
        GET >=> choose [
            subRouteCi "/foo" (
                choose [
                    route "/bar" >=> text "subroute /foo/bar"
                    setStatusCode 404 >=> text "Not found - nested"
                ])
            setStatusCode 404 >=> text "Not found" ]

    let expected = "Not found - nested" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())

[<Fact>]
let ``test subRouteCi: Nested routeCi after subRouteCi is called`` () =
    let testCtx = HttpTestContext(path="/FOO/BAR", method="GET")
    let app =
        GET >=> choose [
            subRouteCi "/foo" (
                routeCi "/bar" >=> text "subroute /foo/bar")
            setStatusCode 404 >=> text "Not found" ]

    let expected = "subroute /foo/bar" |> Encoding.UTF8.GetBytes

    task {
        let! result = app next testCtx

        match result with
        | None     -> failwith $"Result was expected to be {expected}"
        | Some ctx -> testCtx.Body |> equal expected
    } |> (fun tsk -> tsk.RunSynchronously())
