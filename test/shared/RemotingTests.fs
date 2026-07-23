module Fable.Giraffe.Tests.RemotingTests

open System.Text

open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

open Fable.Giraffe.Json

open Fable.Giraffe

// ---------------------------------
// remoting Tests
// ---------------------------------

type Model = { Description: string; Count: int }

/// Nested-record contract: exercises the recursive argument conversion (a record inside a
/// record, and a list of records) rather than the flat single-level case.
type Line = { Sku: string; Qty: int }
type Customer = { Name: string; Model: Model }

type Order =
    { Customer: Customer; Lines: Line list }

type IServer =
    { getNumbers: unit -> Async<int list>
      greet: string -> Async<string>
      updateModel: Model -> Async<Model>
      divide: float -> float -> Async<float>
      meaningOfLife: Async<int>
      countLines: Order -> Async<int>
      boom: string -> Async<string> }

let greetingApi =
    { getNumbers = fun () -> async { return [ 1..5 ] }
      greet =
        fun name ->
            async {
                let greeting = $"Hello, %s{name}"
                return greeting
            }
      updateModel = fun model -> async { return { model with Count = model.Count + 1 } }
      divide = fun x y -> async { return x / y }
      meaningOfLife = async { return 42 }
      // Touches every level of the nested structure, so a dict/object left unconverted
      // anywhere in the tree fails the call rather than silently passing through.
      countLines =
        fun order ->
            async {
                let qty = order.Lines |> List.sumBy (fun l -> l.Qty)
                return qty + order.Customer.Model.Count
            }
      boom = fun _ -> async { return failwith "kaboom" } }

let remotingApp =
    Remoting.createApi ()
    |> Remoting.fromValue greetingApi
    |> Remoting.buildHttpHandler

let tests =
    testList (
        "Remoting",
        [ testAsync (
              "GET \"/IServer/meaningOfLife\" returns 42",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/IServer/meaningOfLife")

                          let app =
                              choose
                                  [ Remoting.createApi ()
                                    |> Remoting.fromValue greetingApi
                                    |> Remoting.buildHttpHandler
                                    setStatusCode 404 >=> text "Not found" ]

                          let expected = "42" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/IServer/getNumbers\" returns numbers",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/IServer/getNumbers")

                          let app =
                              choose
                                  [ Remoting.createApi ()
                                    |> Remoting.fromValue greetingApi
                                    |> Remoting.buildHttpHandler
                                    setStatusCode 404 >=> text "Not found" ]

                          // Built via `serialize` rather than a literal: Python's json.dumps pads
                          // separators ("[1, 2, ...]") while JS's JSON.stringify is compact.
                          let expected = [ 1..5 ] |> serialize |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "POST \"/greet\" returns \"Hello World\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody =
                              TestContext.create (path = "/IServer/greet", method = "POST", body = """["World"]""")

                          let app =
                              Remoting.createApi ()
                              |> Remoting.fromValue greetingApi
                              |> Remoting.buildHttpHandler

                          let expected = "\"Hello, World\"" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "POST \"/updateModel\" returns updated model",
              fun _ ->
                  toAsync (
                      task {
                          let model = { Description = "Test"; Count = 0 }
                          let bytes = model |> List.singleton |> serialize

                          let testCtx, readBody =
                              TestContext.create (path = "/IServer/updateModel", method = "POST", body = bytes)

                          let app =
                              Remoting.createApi ()
                              |> Remoting.fromValue greetingApi
                              |> Remoting.buildHttpHandler

                          let expected =
                              { model with Count = 1 }
                              |> serialize
                              |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "POST \"/divide\" returns float",
              fun _ ->
                  toAsync (
                      task {
                          let bytes = [ 5.0; 2.0 ] |> serialize

                          let testCtx, readBody =
                              TestContext.create (path = "/IServer/divide", method = "POST", body = bytes)

                          let app =
                              Remoting.createApi ()
                              |> Remoting.fromValue greetingApi
                              |> Remoting.buildHttpHandler

                          let expected = "2.5" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "POST \"/countLines\" reconstructs nested records and lists of records",
              fun _ ->
                  toAsync (
                      task {
                          let order =
                              { Customer =
                                  { Name = "Ada"
                                    Model = { Description = "d"; Count = 10 } }
                                Lines = [ { Sku = "a"; Qty = 2 }; { Sku = "b"; Qty = 3 } ] }

                          let body = order |> List.singleton |> serialize

                          let testCtx, readBody =
                              TestContext.create (path = "/IServer/countLines", method = "POST", body = body)

                          // 2 + 3 (line quantities) + 10 (nested Model.Count)
                          let expected = 15 |> serialize |> Encoding.UTF8.GetBytes

                          let! result = remotingApp next testCtx

                          match result with
                          | None -> failwith "Expected the nested-record call to be handled"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "POST with a malformed body returns 400",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, _ =
                              TestContext.create (path = "/IServer/greet", method = "POST", body = "not json at all")

                          let! result = remotingApp next testCtx

                          match result with
                          | None -> failwith "Expected a 400 response rather than an unhandled failure"
                          | Some _ -> assertThat testCtx.Response.StatusCode (isEqualTo 400)
                      }
                  )
          )

          testAsync (
              "POST with the wrong argument count returns 400",
              fun _ ->
                  toAsync (
                      task {
                          // `divide` takes two arguments; supply one.
                          let body = [ 5.0 ] |> serialize

                          let testCtx, _ =
                              TestContext.create (path = "/IServer/divide", method = "POST", body = body)

                          let! result = remotingApp next testCtx

                          match result with
                          | None -> failwith "Expected a 400 response rather than an unhandled failure"
                          | Some _ -> assertThat testCtx.Response.StatusCode (isEqualTo 400)
                      }
                  )
          )

          testAsync (
              "A throwing API method returns 500 without leaking the exception",
              fun _ ->
                  toAsync (
                      task {
                          let body = [ "x" ] |> serialize

                          let testCtx, readBody =
                              TestContext.create (path = "/IServer/boom", method = "POST", body = body)

                          let error: Remoting.RemotingError = { error = "Internal server error" }
                          let expected = error |> serialize |> Encoding.UTF8.GetBytes

                          let! result = remotingApp next testCtx

                          match result with
                          | None -> failwith "Expected a 500 response rather than an unhandled failure"
                          | Some _ ->
                              assertThat testCtx.Response.StatusCode (isEqualTo 500)
                              assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "A configured error handler replaces the default 500",
              fun _ ->
                  toAsync (
                      task {
                          let body = [ "x" ] |> serialize

                          let testCtx, readBody =
                              TestContext.create (path = "/IServer/boom", method = "POST", body = body)

                          let app =
                              Remoting.createApi ()
                              |> Remoting.fromValue greetingApi
                              |> Remoting.withErrorHandler (fun _ex -> setStatusCode 503 >=> text "handled")
                              |> Remoting.buildHttpHandler

                          let! result = app next testCtx

                          match result with
                          | None -> failwith "Expected the custom error handler to respond"
                          | Some _ ->
                              assertThat testCtx.Response.StatusCode (isEqualTo 503)
                              assertThat (readBody ()) (isEqualTo (Encoding.UTF8.GetBytes "handled"))
                      }
                  )
          ) ]
    )
