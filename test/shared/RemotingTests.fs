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

type IServer =
    { getNumbers: unit -> Async<int list>
      greet: string -> Async<string>
      updateModel: Model -> Async<Model>
      divide: float -> float -> Async<float>
      meaningOfLife: Async<int> }

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
      meaningOfLife = async { return 42 } }

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
          ) ]
    )
