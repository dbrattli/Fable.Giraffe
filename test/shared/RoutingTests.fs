module Fable.Giraffe.Tests.RoutingTests

open System
open System.Text

open Scriptorium.Quill
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

open Fable.Giraffe

let private routeTests =
    testList (
        "route",
        [ testAsync (
              "GET \"/\" returns \"Hello World\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "Hello World" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/foo\" returns \"bar\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/foo")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "bar" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/FOO\" returns 404 \"Not found\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/FOO")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
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
          ) ]
    )

let private routeCiTests =
    testList (
        "routeCi",
        [ testAsync (
              "GET \"/JSON\" returns \"BaR\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/JSON")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        route "/json" >=> text "FOO"
                                        routeCi "/json" >=> text "BaR"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "BaR" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          ) ]
    )

let private routexTests =
    testList (
        "routex",
        [ testAsync (
              "GET \"/\" returns \"Hello World\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/")

                          let app =
                              GET
                              >=> choose
                                      [ routex "/" >=> text "Hello World"
                                        routex "/foo" >=> text "bar"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "Hello World" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/foo\" returns \"bar\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/foo")

                          let app =
                              GET
                              >=> choose
                                      [ routex "/" >=> text "Hello World"
                                        routex "/foo" >=> text "bar"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "bar" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/FOO\" returns 404 \"Not found\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/FOO")

                          let app =
                              GET
                              >=> choose
                                      [ routex "/" >=> text "Hello World"
                                        routex "/foo" >=> text "bar"
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
              "GET \"/foo///\" returns \"bar\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/foo///")

                          let app =
                              GET
                              >=> choose
                                      [ routex "/" >=> text "Hello World"
                                        routex "/foo(/*)" >=> text "bar"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "bar" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/foo2\" returns \"bar\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/foo2")

                          let app =
                              GET
                              >=> choose
                                      [ routex "/" >=> text "Hello World"
                                        routex "/foo2(/*)" >=> text "bar"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "bar" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          ) ]
    )

let private routeCixTests =
    testList (
        "routeCix",
        [ testAsync (
              "GET \"/CaSe///\" returns \"right\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/CaSe///")

                          let app =
                              GET
                              >=> choose
                                      [ routex "/case(/*)" >=> text "wrong"
                                        routeCix "/case(/*)" >=> text "right"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "right" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          ) ]
    )

let private routefTests =
    testList (
        "routef",
        [ testAsync (
              "GET \"/foo/blah blah/bar\" returns \"blah blah\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/foo/blah blah/bar")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        routef "/foo/%s/bar" text
                                        routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "blah blah" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/foo/johndoe/59\" returns \"Name: johndoe, Age: 59\"",
              // BEAM: routef typed captures diverge — %i / %O (Guid) / %u parsing is not
              // implemented on the Erlang backend (%s works).
              skipIfBeam,
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/foo/johndoe/59")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        routef "/foo/%s/bar" text
                                        routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "Name: johndoe, Age: 59" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/foo/b%2Fc/bar\" returns \"b%2Fc\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/foo/b%2Fc/bar")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        routef "/foo/%s/bar" text
                                        routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "b/c" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/foo/a%2Fb%2Bc.d%2Ce/bar\" returns \"a%2Fb%2Bc.d%2Ce\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/foo/a%2Fb%2Bc.d%2Ce/bar")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        routef "/foo/%s/bar" text
                                        routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "a/b%2Bc.d%2Ce" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/foo/%O/bar/%O\" returns \"Guid1: ..., Guid2: ...\"",
              // JS: FormatExpressions Guid matching returns 404 on the JavaScript target.
              // BEAM: routef typed captures diverge (see above).
              skipIfJavaScript >> skipIfBeam,
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody =
                              TestContext.create (path = "/foo/4ec87f064d1e41b49342ab1aead1f99d/bar/2a6c9185-95d9-4d8c-80a6-575f99c2a716")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        routef "/foo/%s/bar" text
                                        routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
                                        routef "/foo/%O/bar/%O" (fun (guid1: Guid, guid2: Guid) ->
                                            text (sprintf "Guid1: %O, Guid2: %O" guid1 guid2))
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected =
                              "Guid1: 4ec87f06-4d1e-41b4-9342-ab1aead1f99d, Guid2: 2a6c9185-95d9-4d8c-80a6-575f99c2a716"
                              |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/foo/%u/bar/%u\" returns \"Id1: ..., Id2: ...\"",
              // BEAM: routef typed captures diverge (see above).
              skipIfBeam,
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody =
                              TestContext.create (path = "/foo/r1iKapqh_s4/bar/5aLu720NzTs")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        routef "/foo/%s/bar" text
                                        routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
                                        routef "/foo/%u/bar/%u" (fun (id1: uint64, id2: uint64) ->
                                            text (sprintf "Id1: %u, Id2: %u" id1 id2))
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected =
                              "Id1: 12635000945053400782, Id2: 16547050693006839099"
                              |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/foo/bar/baz/qux\" returns 404 \"Not found\"",
              // BEAM: routef typed captures diverge (see above).
              skipIfBeam,
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/foo/bar/baz/qux")

                          let app =
                              GET
                              >=> choose
                                      [ routef "/foo/%s/%s" (fun (s1, s2) -> text (sprintf "%s,%s" s1 s2))
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
          ) ]
    )

let private routeCifTests =
    testList (
        "routeCif",
        [ testAsync (
              "POST \"/POsT/1\" returns \"1\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/POsT/1", method = "POST")

                          let app =
                              choose
                                  [ GET
                                    >=> choose [ route "/" >=> text "Hello World" ]
                                    POST
                                    >=> choose [ route "/post/1" >=> text "2"; routeCif "/post/%i" json ]
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
              "POST \"/POsT/523\" returns \"523\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/POsT/523", method = "POST")

                          let app =
                              choose
                                  [ GET
                                    >=> choose [ route "/" >=> text "Hello World" ]
                                    POST
                                    >=> choose [ route "/post/1" >=> text "1"; routeCif "/post/%i" json ]
                                    setStatusCode 404 >=> text "Not found" ]

                          let expected = "523" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          ) ]
    )

let private subRouteTests =
    testList (
        "subRoute",
        [ testAsync (
              "Route with empty route",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/api", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        subRoute
                                            "/api"
                                            (choose
                                                [ route "" >=> text "api root"
                                                  route "/admin" >=> text "admin"
                                                  route "/users" >=> text "users" ])
                                        route "/api/test" >=> text "test"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "api root" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "Normal nested route after subRoute",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/api/users", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        subRoute
                                            "/api"
                                            (choose
                                                [ route "" >=> text "api root"
                                                  route "/admin" >=> text "admin"
                                                  route "/users" >=> text "users" ])
                                        route "/api/test" >=> text "test"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "users" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "Route after subRoute has same beginning of path",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/api/test", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        subRoute
                                            "/api"
                                            (choose
                                                [ route "" >=> text "api root"
                                                  route "/admin" >=> text "admin"
                                                  route "/users" >=> text "users" ])
                                        route "/api/test" >=> text "test"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "test" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "Nested sub routes",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/api/v2/users", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        subRoute
                                            "/api"
                                            (choose
                                                [ route "" >=> text "api root"
                                                  route "/admin" >=> text "admin"
                                                  route "/users" >=> text "users"
                                                  subRoute
                                                      "/v2"
                                                      (choose
                                                          [ route "" >=> text "api root v2"
                                                            route "/admin" >=> text "admin v2"
                                                            route "/users" >=> text "users v2" ]) ])
                                        route "/api/test" >=> text "test"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "users v2" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "Multiple nested sub routes",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/api/v2/admin2", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        subRoute
                                            "/api"
                                            (choose
                                                [ route "/users" >=> text "users"
                                                  subRoute
                                                      "/v2"
                                                      (choose [ route "/admin" >=> text "admin v2"; route "/users" >=> text "users v2" ])
                                                  subRoute "/v2" (route "/admin2" >=> text "correct admin2") ])
                                        route "/api/test" >=> text "test"
                                        route "/api/v2/else" >=> text "else"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "correct admin2" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "Route after nested sub routes has same beginning of path",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/api/v2/else", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        subRoute
                                            "/api"
                                            (choose
                                                [ route "" >=> text "api root"
                                                  route "/admin" >=> text "admin"
                                                  route "/users" >=> text "users"
                                                  subRoute
                                                      "/v2"
                                                      (choose
                                                          [ route "" >=> text "api root v2"
                                                            route "/admin" >=> text "admin v2"
                                                            route "/users" >=> text "users v2" ])
                                                  route "/yada" >=> text "yada" ])
                                        route "/api/test" >=> text "test"
                                        route "/api/v2/else" >=> text "else"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "else" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "routef inside subRoute",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody =
                              TestContext.create (path = "/api/foo/bar/yadayada", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ route "/" >=> text "Hello World"
                                        route "/foo" >=> text "bar"
                                        subRoute "/api" (choose [ route "" >=> text "api root"; routef "/foo/bar/%s" text ])
                                        route "/api/test" >=> text "test"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "yadayada" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          ) ]
    )

let private subRoutefTests =
    testList (
        "subRoutef",
        [ testAsync (
              "GET \"/\" returns \"Not found\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ subRoutef "/%s/%i" (fun (lang, version) ->
                                            choose
                                                [ route "/foo" >=> text "bar"
                                                  routef "/%s" (fun name ->
                                                      text (sprintf "Hello %s! Lang: %s, Version: %i" name lang version)) ])
                                        route "/bar" >=> text "foo"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "Not found" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/bar\" returns \"foo\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/bar", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ subRoutef "/%s/%i" (fun (lang, version) ->
                                            choose
                                                [ route "/foo" >=> text "bar"
                                                  routef "/%s" (fun name ->
                                                      text (sprintf "Hello %s! Lang: %s, Version: %i" name lang version)) ])
                                        route "/bar" >=> text "foo"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "foo" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/John/5/foo\" returns \"bar\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/John/5/foo", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ subRoutef "/%s/%i" (fun (lang, version) ->
                                            choose
                                                [ route "/foo" >=> text "bar"
                                                  routef "/%s" (fun name ->
                                                      text (sprintf "Hello %s! Lang: %s, Version: %i" name lang version)) ])
                                        route "/bar" >=> text "foo"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "bar" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/en/10/Julia\" returns \"Hello Julia! Lang: en, Version: 10\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/en/10/Julia", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ subRoutef "/%s/%i" (fun (lang, version) ->
                                            choose
                                                [ route "/foo" >=> text "bar"
                                                  routef "/%s" (fun name ->
                                                      text (sprintf "Hello %s! Lang: %s, Version: %i" name lang version)) ])
                                        route "/bar" >=> text "foo"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected =
                              "Hello Julia! Lang: en, Version: 10"
                              |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "GET \"/en/10/api/Julia\" returns \"Hello Julia! Lang: en, Version: 10\"",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody =
                              TestContext.create (path = "/en/10/api/Julia", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ subRoutef "/%s/%i/api" (fun (lang, version) ->
                                            choose
                                                [ route "/foo" >=> text "bar"
                                                  routef "/%s" (fun name ->
                                                      text (sprintf "Hello %s! Lang: %s, Version: %i" name lang version)) ])
                                        route "/bar" >=> text "foo"
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected =
                              "Hello Julia! Lang: en, Version: 10"
                              |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          ) ]
    )

let private subRouteCiTests =
    testList (
        "subRouteCi",
        [ testAsync (
              "Non-filtering handler after subRouteCi is called",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/FOO", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ subRouteCi "/foo" (text "subroute /foo")
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "subroute /foo" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "Nested route after subRouteCi is called",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/FOO/bar", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ subRouteCi "/foo" (route "/bar" >=> text "subroute /foo/bar")
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "subroute /foo/bar" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "Nested route after subRouteCi is still case sensitive",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/FOO/BAR", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ subRouteCi
                                            "/foo"
                                            (choose
                                                [ route "/bar" >=> text "subroute /foo/bar"
                                                  setStatusCode 404 >=> text "Not found - nested" ])
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "Not found - nested" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "Nested routeCi after subRouteCi is called",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/FOO/BAR", method = "GET")

                          let app =
                              GET
                              >=> choose
                                      [ subRouteCi "/foo" (routeCi "/bar" >=> text "subroute /foo/bar")
                                        setStatusCode 404 >=> text "Not found" ]

                          let expected = "subroute /foo/bar" |> Encoding.UTF8.GetBytes

                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          ) ]
    )

let tests =
    testList (
        "Routing",
        [ routeTests
          routeCiTests
          routexTests
          routeCixTests
          routefTests
          routeCifTests
          subRouteTests
          subRoutefTests
          subRouteCiTests ]
    )
