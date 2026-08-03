module Fable.Giraffe.Tests.HandlerTests

open System.Text

open Scriptorium.Quill
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

open Fable.Giraffe

// ---------------------------------
// Test Types
// ---------------------------------

type Dummy = { foo: string; bar: string; age: int }

/// Resolved out of the service collection by the DI test below. A record so it is an
/// immutable value on every backend — on BEAM a service that is a class with mutable fields
/// is a process-dictionary ref and cannot be shared across processes at all.
type Greeter = { Greeting: string }

// ---------------------------------
// Tests
// ---------------------------------

let tests =
    testList (
        "Handlers",
        [ testAsync (
              "GET \"/json\" returns json object",
              // JSON formatting diverges from the Python reference on both targets: JS
              // `JSON.stringify` is compact where Python's `json.dumps` adds ", "/": "
              // spacing, and the BEAM jsx serializer differs again. Cross-target JSON parity
              // is tracked separately.
              skipIfJavaScript >> skipIfBeam,
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/json")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        route "/json"
                                        >=> json { foo = "john"; bar = "doe"; age = 30 }
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected =
                              "{\"foo\": \"john\", \"bar\": \"doe\", \"age\": 30}"
                              |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "POST \"/post/1\" returns \"1\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/post/1", method = "POST")

                          let app =
                              choose
                                  [ GET
                                    >=> choose [ route "/" >=> text "Hello World"; route "/foo" >=> text "bar" ]
                                    POST
                                    >=> choose [ route "/post/1" >=> text "1"; route "/post/2" >=> text "2" ]
                                    setStatusCode 404 >=> text "Not found" ]

                          let expected = "1" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "POST \"/post/2\" returns \"2\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/post/2", method = "POST")

                          let app =
                              choose
                                  [ GET
                                    >=> choose [ route "/" >=> text "Hello World"; route "/foo" >=> text "bar" ]
                                    POST
                                    >=> choose [ route "/post/1" >=> text "1"; route "/post/2" >=> text "2" ]
                                    setStatusCode 404 >=> text "Not found" ]

                          let expected = "2" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "PUT \"/post/2\" returns 404 \"Not found\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/post/2", method = "PUT")

                          let app =
                              choose
                                  [ GET
                                    >=> choose [ route "/" >=> text "Hello World"; route "/foo" >=> text "bar" ]
                                    POST
                                    >=> choose [ route "/post/1" >=> text "1"; route "/post/2" >=> text "2" ]
                                    setStatusCode 404 >=> text "Not found" ]

                          let expected = "Not found" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some ctx ->
                              assertThat (readBody ()) (isEqualTo expected)
                              assertThat ctx.Response.StatusCode (isEqualTo 404)
                      }
                  )
          )

          testAsync (
              "POST \"/text\" with supported Accept header returns \"text\"",
              // BEAM's HttpRequest.GetTypedHeaders() is a stub returning empty, so
              // mustAccept/Accept-negotiation cannot resolve (tracked: implement Cowboy headers).
              skipIfBeam,
              fun _ ->
                  toAsync (
                      task {
                          let headers = HeaderDictionary()
                          headers.Add("Accept", StringValues("text/plain"))

                          let testCtx, readBody =
                              TestContext.create (path = "/text", method = "POST", headers = headers)

                          let app =
                              choose
                                  [ GET
                                    >=> choose [ route "/" >=> text "Hello World"; route "/foo" >=> text "bar" ]
                                    POST
                                    >=> choose
                                            [ route "/text"
                                              >=> mustAccept [ "text/plain" ]
                                              >=> text "text"
                                              route "/json"
                                              >=> mustAccept [ "application/json" ]
                                              >=> json "json"
                                              route "/either"
                                              >=> mustAccept [ "text/plain"; "application/json" ]
                                              >=> text "either" ]
                                    setStatusCode 404 >=> text "Not found" ]

                          let expected = "text" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some ctx ->
                              assertThat (readBody ()) (isEqualTo expected)
                              assertThat (getContentType ctx.Response) (isEqualTo "text/plain; charset=utf-8")
                      }
                  )
          )

          testAsync (
              "POST \"/json\" with supported Accept header returns \"json\"",
              // BEAM: headers stub, see above.
              skipIfBeam,
              fun _ ->
                  toAsync (
                      task {
                          let headers = HeaderDictionary()
                          headers.Add("Accept", StringValues("application/json"))

                          let testCtx, readBody =
                              TestContext.create (path = "/json", method = "POST", headers = headers)

                          let app =
                              choose
                                  [ GET
                                    >=> choose [ route "/" >=> text "Hello World"; route "/foo" >=> text "bar" ]
                                    POST
                                    >=> choose
                                            [ route "/text"
                                              >=> mustAccept [ "text/plain" ]
                                              >=> text "text"
                                              route "/json"
                                              >=> mustAccept [ "application/json" ]
                                              >=> json "json"
                                              route "/either"
                                              >=> mustAccept [ "text/plain"; "application/json" ]
                                              >=> text "either" ]
                                    setStatusCode 404 >=> text "Not found" ]

                          let expected = "\"json\"" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some ctx ->
                              assertThat (readBody ()) (isEqualTo expected)
                              assertThat (getContentType ctx.Response) (isEqualTo "application/json; charset=utf-8")
                      }
                  )
          )

          testAsync (
              "POST \"/either\" with supported Accept header returns \"either\"",
              // BEAM: headers stub, see above.
              skipIfBeam,
              fun _ ->
                  toAsync (
                      task {
                          let headers = HeaderDictionary()
                          headers.Add("Accept", StringValues("application/json"))

                          let testCtx, readBody =
                              TestContext.create (path = "/either", method = "POST", headers = headers)

                          let app =
                              choose
                                  [ GET
                                    >=> choose [ route "/" >=> text "Hello World"; route "/foo" >=> text "bar" ]
                                    POST
                                    >=> choose
                                            [ route "/text"
                                              >=> mustAccept [ "text/plain" ]
                                              >=> text "text"
                                              route "/json"
                                              >=> mustAccept [ "application/json" ]
                                              >=> json "json"
                                              route "/either"
                                              >=> mustAccept [ "text/plain"; "application/json" ]
                                              >=> text "either" ]
                                    setStatusCode 404 >=> text "Not found" ]

                          let expected = "either" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some ctx ->
                              assertThat (readBody ()) (isEqualTo expected)
                              assertThat (getContentType ctx.Response) (isEqualTo "text/plain; charset=utf-8")
                      }
                  )
          )

          testAsync (
              "POST \"/either\" with unsupported Accept header returns 404 \"Not found\"",
              fun _ ->
                  toAsync (
                      task {
                          let headers = HeaderDictionary()
                          headers.Add("Accept", StringValues("application/xml"))

                          let testCtx, readBody =
                              TestContext.create (path = "/either", method = "POST", headers = headers)

                          let app =
                              choose
                                  [ GET
                                    >=> choose [ route "/" >=> text "Hello World"; route "/foo" >=> text "bar" ]
                                    POST
                                    >=> choose
                                            [ route "/text"
                                              >=> mustAccept [ "text/plain" ]
                                              >=> text "text"
                                              route "/json"
                                              >=> mustAccept [ "application/json" ]
                                              >=> json "json"
                                              route "/either"
                                              >=> mustAccept [ "text/plain"; "application/json" ]
                                              >=> text "either" ]
                                    setStatusCode 404 >=> text "Not found" ]

                          let expected = "Not found" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some ctx ->
                              assertThat (readBody ()) (isEqualTo expected)
                              assertThat ctx.Response.StatusCode (isEqualTo 404)
                      }
                  )
          )

          testAsync (
              "GET \"/redirect\" redirects to \"/\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, _ = TestContext.create (path = "/redirect", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/redirect" >=> redirectTo false "/"
                                        setStatusCode 404 >=> text "Not found" ]

                          let! result = app next testCtx

                          match result with
                          | None -> failwith "It was expected that the request would be redirected"
                          | Some ctx -> assertThat ctx.Response.StatusCode (isEqualTo 302)
                      // TODO: ctx.Response.Headers
                      }
                  )
          )

          testAsync (
              "POST \"/redirect\" redirects to \"/\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, _ = TestContext.create (path = "/redirect", method = "POST")

                          let app =
                              POST
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/redirect" >=> redirectTo true "/"
                                        setStatusCode 404 >=> text "Not found" ]

                          let! result = app next testCtx

                          match result with
                          | None -> failwith "It was expected that the request would be redirected"
                          | Some ctx -> assertThat ctx.Response.StatusCode (isEqualTo 301)
                      // TODO: ctx.Response.Headers
                      }
                  )
          )

          // Covers the DI surface itself (AddSingleton -> ctx.GetService) on all three backends.
          // It does NOT cover BEAM's real failure mode: under Cowboy the collection is built in
          // the builder process and read in a per-request one, and a ServiceCollection is a
          // process-dictionary ref that cannot cross that boundary. GiraffeHandler carries a
          // portable snapshot and rebuilds the collection per request to fix that, but this test
          // bypasses GiraffeHandler entirely, so registration and resolution share a process
          // here. The cross-process path is exercised by running the example app.
          testAsync (
              "ctx.GetService resolves a registered singleton",
              fun _ ->
                  toAsync (
                      task {
                          let services = ServiceCollection()
                          services.AddSingleton<Greeter> { Greeting = "Hello from DI" }

                          let testCtx, readBody = TestContext.create (path = "/di", services = services)

                          let app =
                              choose
                                  [ route "/di"
                                    >=> fun next ctx -> text (ctx.GetService<Greeter>()).Greeting next ctx
                                    setStatusCode 404 >=> text "Not found" ]

                          let expected = "Hello from DI" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          ) ]
    )
