module Fable.Giraffe.Tests.EndpointTests

open System.Text

open Scriptorium.Quill
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

open Fable.Giraffe
open Fable.Giraffe.FormatExpressions

// The endpoint DSL deliberately shadows `route`, `routef`, `subRoute` and the verb filters, so it
// is referenced module-qualified throughout rather than opened — the equivalence tests below need
// both the classic combinators and their endpoint counterparts in scope at once.

// ---------------------------------
// Helpers for inspecting the description
// ---------------------------------

let rec private metas (endpoint: Endpoint) : EndpointMeta list =
    match endpoint with
    | Endpoint.Simple(_, _, meta, _) -> [ meta ]
    | Endpoint.Template(_, _, _, meta, _) -> [ meta ]
    | Endpoint.Nested(_, meta, endpoints) -> meta :: (endpoints |> List.collect metas)
    | Endpoint.Multi endpoints -> endpoints |> List.collect metas

let rec private verbs (endpoint: Endpoint) : HttpVerb list =
    match endpoint with
    | Endpoint.Simple(verb, _, _, _) -> [ verb ]
    | Endpoint.Template(verb, _, _, _, _) -> [ verb ]
    | Endpoint.Nested(_, _, endpoints) -> endpoints |> List.collect verbs
    | Endpoint.Multi endpoints -> endpoints |> List.collect verbs

// ---------------------------------
// Format-char extraction
// ---------------------------------

let private formatCharTests =
    testList (
        "getFormatChars",
        [ test ("a literal path has no format chars", (fun _ -> assertThat (getFormatChars "/ping") (isEqualTo [])))

          test ("a single parameter is extracted", fun _ -> assertThat (getFormatChars "/user/%i") (isEqualTo [ 'i' ]))

          test ("parameters are returned in template order", fun _ -> assertThat (getFormatChars "/foo/%s/bar/%i") (isEqualTo [ 's'; 'i' ]))

          test ("an escaped %% is a literal, not a parameter", fun _ -> assertThat (getFormatChars "/discount/100%%/off") (isEqualTo []))

          test (
              "every supported format char is recognised",
              fun _ -> assertThat (getFormatChars "%b%c%s%i%d%f%O%u") (isEqualTo [ 'b'; 'c'; 's'; 'i'; 'd'; 'f'; 'O'; 'u' ])
          ) ]
    )

// ---------------------------------
// Description: verbs and metadata
// ---------------------------------

let private descriptionTests =
    testList (
        "description",
        [ test (
              "a verb group stamps its verb on every leaf",
              fun _ ->
                  let endpoints =
                      Endpoints.GET [ Endpoints.route "/a" (text "a"); Endpoints.route "/b" (text "b") ]

                  assertThat (verbs endpoints) (isEqualTo [ HttpVerb.GET; HttpVerb.GET ])
          )

          test (
              "an ungrouped route answers any verb",
              fun _ -> assertThat (verbs (Endpoints.route "/a" (text "a"))) (isEqualTo [ HttpVerb.ANY ])
          )

          test (
              "a verb group reaches leaves nested under a sub-route",
              fun _ ->
                  let endpoints =
                      Endpoints.subRoute "/api" [ Endpoints.GET [ Endpoints.route "/ping" (text "pong") ] ]

                  assertThat (verbs endpoints) (isEqualTo [ HttpVerb.GET ])
          )

          test (
              "summary is recorded",
              fun _ ->
                  let endpoint =
                      Endpoints.route "/a" (text "a")
                      |> Endpoints.summary "Health check"

                  assertThat ((metas endpoint).Head.Summary) (isEqualTo (Some "Health check"))
          )

          // configureEndpoint recurses into groups, so metadata applied to `GET [ a; b ]` lands on
          // both leaves rather than being silently dropped on the group.
          test (
              "metadata applied to a group reaches every leaf",
              fun _ ->
                  let endpoints =
                      Endpoints.GET [ Endpoints.route "/a" (text "a"); Endpoints.route "/b" (text "b") ]
                      |> Endpoints.tags [ "core" ]

                  assertThat (metas endpoints |> List.map (fun m -> m.Tags)) (isEqualTo [ [ "core" ]; [ "core" ] ])
          )

          test (
              "responses accumulate in declaration order",
              fun _ ->
                  let endpoint =
                      Endpoints.route "/a" (text "a")
                      |> Endpoints.responds<string> 200
                      |> Endpoints.respondsEmpty 404

                  let codes =
                      (metas endpoint).Head.Responses
                      |> List.map (fun (code, _, _) -> code)

                  assertThat codes (isEqualTo [ 200; 404 ])
          )

          test (
              "accepts records the request body type",
              fun _ ->
                  let endpoint =
                      Endpoints.route "/a" (text "a")
                      |> Endpoints.accepts<string>

                  let contentType = (metas endpoint).Head.Accepts |> Option.map snd

                  assertThat contentType (isEqualTo (Some "application/json"))
          )

          test (
              "path parameter names are recorded",
              fun _ ->
                  let endpoint =
                      Endpoints.route "/a" (text "a")
                      |> Endpoints.pathParams [ "id" ]

                  assertThat ((metas endpoint).Head.PathParamNames) (isEqualTo [ "id" ])
          )

          // The whole point of Template: the template survives composition, where a bare
          // HttpHandler would have erased it.
          test (
              "routef retains its template and format chars",
              fun _ ->
                  match Endpoints.routef "/user/%s/posts/%s" (fun (a, b) -> text $"{a}{b}") with
                  | Endpoint.Template(_, template, chars, _, _) ->
                      assertThat (template, chars) (isEqualTo ("/user/%s/posts/%s", [ 's'; 's' ]))
                  | _ -> failwith "routef should produce an Endpoint.Template"
          ) ]
    )

// ---------------------------------
// Lowering
// ---------------------------------

let private loweringTests =
    testList (
        "toHandler",
        [ // The load-bearing test: the endpoint layer must route exactly as the hand-written
          // composition it lowers to, since it reuses the same combinators rather than
          // reimplementing matching.
          testAsync (
              "lowers to the same behaviour as hand-written composition",
              fun _ ->
                  toAsync (
                      task {
                          let endpointCtx, readEndpointBody = TestContext.create (path = "/foo")
                          let classicCtx, readClassicBody = TestContext.create (path = "/foo")

                          let endpointApp =
                              Endpoints.toHandler
                                  [ Endpoints.GET
                                        [ Endpoints.route "/" (text "Hello World")
                                          Endpoints.route "/foo" (text "bar") ] ]

                          let classicApp =
                              GET
                              >=> choose [ route "/" >=> text "Hello World"; route "/foo" >=> text "bar" ]

                          let! endpointResult = endpointApp next endpointCtx
                          let! classicResult = classicApp next classicCtx

                          match endpointResult, classicResult with
                          | Some _, Some _ -> assertThat (readEndpointBody ()) (isEqualTo (readClassicBody ()))
                          | _ -> failwith "Both pipelines were expected to match"
                      }
                  )
          )

          testAsync (
              "matches a literal route",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/ping")

                          let app =
                              Endpoints.toHandler [ Endpoints.GET [ Endpoints.route "/ping" (text "pong") ] ]

                          let expected = "pong" |> Encoding.UTF8.GetBytes
                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "skips a route whose verb does not match",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, _ = TestContext.create (method = "POST", path = "/ping")

                          let app =
                              Endpoints.toHandler [ Endpoints.GET [ Endpoints.route "/ping" (text "pong") ] ]

                          let! result = app next testCtx

                          match result with
                          | Some _ -> failwith "Result was expected to be None"
                          | None -> ()
                      }
                  )
          )

          testAsync (
              "an ungrouped route answers any verb",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (method = "DELETE", path = "/any")

                          let app = Endpoints.toHandler [ Endpoints.route "/any" (text "matched") ]

                          let expected = "matched" |> Encoding.UTF8.GetBytes
                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "a sub-route prefixes its children",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/api/ping")

                          let app =
                              Endpoints.toHandler [ Endpoints.subRoute "/api" [ Endpoints.GET [ Endpoints.route "/ping" (text "pong") ] ] ]

                          let expected = "pong" |> Encoding.UTF8.GetBytes
                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          testAsync (
              "falls through to a later endpoint when the first does not match",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/second")

                          let app =
                              Endpoints.toHandler
                                  [ Endpoints.GET
                                        [ Endpoints.route "/first" (text "first")
                                          Endpoints.route "/second" (text "second") ] ]

                          let expected = "second" |> Encoding.UTF8.GetBytes
                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          )

          // Only %s is exercised here: %i / %O / %u parsing diverges on BEAM (see FOLLOWUPS.md),
          // and that is a `routef` gap this layer inherits rather than one it introduces.
          testAsync (
              "lowers routef onto the existing matcher",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/user/johndoe")

                          let app = Endpoints.toHandler [ Endpoints.GET [ Endpoints.routef "/user/%s" text ] ]

                          let expected = "johndoe" |> Encoding.UTF8.GetBytes
                          let! result = app next testCtx

                          match result with
                          | None -> failwith $"Result was expected to be {expected}"
                          | Some _ -> assertThat (readBody ()) (isEqualTo expected)
                      }
                  )
          ) ]
    )

let tests =
    testList ("Endpoints", [ formatCharTests; descriptionTests; loweringTests ])
