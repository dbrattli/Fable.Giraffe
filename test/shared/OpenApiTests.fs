module Fable.Giraffe.Tests.OpenApiTests

open System.Text

open Scriptorium.Quill
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

open Fable.TypedJson.Schema

open Fable.Giraffe

// The document is a `JsonSchemaValue` tree, so these assert against it directly rather than
// re-parsing the rendered JSON — the same reason the assembler builds it as data.

type Widget = { Name: string; Size: int }

type Crate = { Label: string; Contents: Widget }

let private info = OpenApiInfo.Create("Test API", "1.0")

let private prop (v: JsonSchemaValue) (key: string) : JsonSchemaValue option =
    match v with
    | SVDict m -> Map.tryFind key m
    | _ -> None

/// Walk a chain of object keys, e.g. `at doc [ "paths"; "/ping"; "get" ]`.
let private at (v: JsonSchemaValue) (keys: string list) : JsonSchemaValue option =
    keys
    |> List.fold (fun acc k -> acc |> Option.bind (fun x -> prop x k)) (Some v)

let private str (v: JsonSchemaValue option) : string option =
    match v with
    | Some(SVStr s) -> Some s
    | _ -> None

let private keysOf (v: JsonSchemaValue option) : string list =
    match v with
    | Some(SVDict m) -> m |> Map.toList |> List.map fst
    | _ -> []

// ---------------------------------
// Path templates
// ---------------------------------

let private templateTests =
    testList (
        "toPathTemplate",
        [ test ("a literal path is unchanged", (fun _ -> assertThat (OpenApi.toPathTemplate [] "/ping") (isEqualTo "/ping")))

          test (
              "a named parameter is substituted",
              fun _ -> assertThat (OpenApi.toPathTemplate [ "id" ] "/user/%i") (isEqualTo "/user/{id}")
          )

          test (
              "an unnamed parameter falls back to a positional name",
              fun _ -> assertThat (OpenApi.toPathTemplate [] "/user/%i") (isEqualTo "/user/{p0}")
          )

          test (
              "several parameters are substituted in order",
              fun _ -> assertThat (OpenApi.toPathTemplate [ "lang"; "id" ] "/%s/user/%i") (isEqualTo "/{lang}/user/{id}")
          )

          test (
              "an escaped %% stays literal and consumes no name",
              fun _ -> assertThat (OpenApi.toPathTemplate [ "id" ] "/100%%/user/%i") (isEqualTo "/100%/user/{id}")
          ) ]
    )

// ---------------------------------
// Document structure
// ---------------------------------

let private documentTests =
    testList (
        "buildDocument",
        [ test (
              "emits the OpenAPI version and info block",
              fun _ ->
                  let doc = OpenApi.buildDocument info []

                  assertThat (str (prop doc "openapi")) (isEqualTo (Some "3.1.0"))
                  assertThat (str (at doc [ "info"; "title" ])) (isEqualTo (Some "Test API"))
                  assertThat (str (at doc [ "info"; "version" ])) (isEqualTo (Some "1.0"))
          )

          test (
              "places a route under its path and verb",
              fun _ ->
                  let doc =
                      OpenApi.buildDocument
                          info
                          [ Endpoints.GET
                                [ Endpoints.route "/ping" (text "pong")
                                  |> Endpoints.summary "Health check" ] ]

                  assertThat (str (at doc [ "paths"; "/ping"; "get"; "summary" ])) (isEqualTo (Some "Health check"))
          )

          test (
              "two verbs on one path share a path item",
              fun _ ->
                  let doc =
                      OpenApi.buildDocument
                          info
                          [ Endpoints.GET [ Endpoints.route "/widget" (text "get") ]
                            Endpoints.POST [ Endpoints.route "/widget" (text "post") ] ]

                  assertThat (keysOf (at doc [ "paths"; "/widget" ])) (isEqualTo [ "get"; "post" ])
          )

          test (
              "a sub-route contributes its prefix",
              fun _ ->
                  let doc =
                      OpenApi.buildDocument info [ Endpoints.subRoute "/api" [ Endpoints.GET [ Endpoints.route "/ping" (text "pong") ] ] ]

                  assertThat (keysOf (prop doc "paths")) (isEqualTo [ "/api/ping" ])
          )

          test (
              "tags set on a group are inherited by its leaves",
              fun _ ->
                  let doc =
                      OpenApi.buildDocument
                          info
                          [ Endpoints.subRoute "/api" [ Endpoints.GET [ Endpoints.route "/ping" (text "pong") ] ]
                            |> Endpoints.tags [ "core" ] ]

                  match at doc [ "paths"; "/api/ping"; "get"; "tags" ] with
                  | Some(SVList tags) -> assertThat tags (isEqualTo [ SVStr "core" ])
                  | _ -> failwith "expected inherited tags"
          )

          // OpenAPI has no "any method" operation, so such a route cannot be described.
          // It still routes — it is simply absent from the document.
          test (
              "a route with no verb is omitted",
              fun _ ->
                  let doc = OpenApi.buildDocument info [ Endpoints.route "/any" (text "x") ]

                  assertThat (keysOf (prop doc "paths")) (isEqualTo [])
          )

          test (
              "an operation with no declared response still gets one",
              fun _ ->
                  let doc =
                      OpenApi.buildDocument info [ Endpoints.GET [ Endpoints.route "/ping" (text "pong") ] ]

                  assertThat (str (at doc [ "paths"; "/ping"; "get"; "responses"; "200"; "description" ])) (isEqualTo (Some "Success"))
          ) ]
    )

// ---------------------------------
// Parameters, bodies and components
// ---------------------------------

let private schemaTests =
    testList (
        "schemas",
        [ test (
              "a route template becomes a typed path parameter",
              fun _ ->
                  let doc =
                      OpenApi.buildDocument
                          info
                          [ Endpoints.GET
                                [ Endpoints.routef "/user/%i" (fun (_: int) -> text "x")
                                  |> Endpoints.pathParams [ "id" ] ] ]

                  match at doc [ "paths"; "/user/{id}"; "get"; "parameters" ] with
                  | Some(SVList [ p ]) ->
                      assertThat (str (prop p "name")) (isEqualTo (Some "id"))
                      assertThat (str (prop p "in")) (isEqualTo (Some "path"))
                      assertThat (prop p "required") (isEqualTo (Some(SVBool true)))
                      assertThat (str (at p [ "schema"; "type" ])) (isEqualTo (Some "integer"))
                      assertThat (str (at p [ "schema"; "format" ])) (isEqualTo (Some "int32"))
                  | _ -> failwith "expected exactly one path parameter"
          )

          // The payoff of the $ref work: a record is defined once under components and
          // referenced, rather than inlined at each mention.
          test (
              "a response type is hoisted into components and referenced",
              fun _ ->
                  let doc =
                      OpenApi.buildDocument
                          info
                          [ Endpoints.GET
                                [ Endpoints.route "/widget" (text "x")
                                  |> Endpoints.responds<Widget> 200 ] ]

                  assertThat
                      (str (
                          at
                              doc
                              [ "paths"
                                "/widget"
                                "get"
                                "responses"
                                "200"
                                "content"
                                "application/json"
                                "schema"
                                "$ref" ]
                      ))
                      (isEqualTo (Some "#/components/schemas/Widget"))

                  assertThat (keysOf (at doc [ "components"; "schemas" ])) (isEqualTo [ "Widget" ])
          )

          test (
              "a nested type is hoisted alongside its parent",
              fun _ ->
                  let doc =
                      OpenApi.buildDocument
                          info
                          [ Endpoints.GET
                                [ Endpoints.route "/crate" (text "x")
                                  |> Endpoints.responds<Crate> 200 ] ]

                  assertThat (keysOf (at doc [ "components"; "schemas" ])) (isEqualTo [ "Crate"; "Widget" ])
          )

          // Property names come off the same walk the serializer uses, so the document
          // cannot name a field the wire does not carry.
          test (
              "component properties use the serializer's wire names",
              fun _ ->
                  let doc =
                      OpenApi.buildDocument
                          info
                          [ Endpoints.GET
                                [ Endpoints.route "/widget" (text "x")
                                  |> Endpoints.responds<Widget> 200 ] ]

                  assertThat (keysOf (at doc [ "components"; "schemas"; "Widget"; "properties" ])) (isEqualTo [ "name"; "size" ])
          )

          test (
              "a request body type becomes requestBody",
              fun _ ->
                  let doc =
                      OpenApi.buildDocument
                          info
                          [ Endpoints.POST
                                [ Endpoints.route "/widget" (text "x")
                                  |> Endpoints.accepts<Widget>
                                  |> Endpoints.responds<Widget> 201 ] ]

                  assertThat
                      (str (
                          at
                              doc
                              [ "paths"
                                "/widget"
                                "post"
                                "requestBody"
                                "content"
                                "application/json"
                                "schema"
                                "$ref" ]
                      ))
                      (isEqualTo (Some "#/components/schemas/Widget"))
          )

          test (
              "an empty response declares no content",
              fun _ ->
                  let doc =
                      OpenApi.buildDocument
                          info
                          [ Endpoints.GET
                                [ Endpoints.route "/widget" (text "x")
                                  |> Endpoints.respondsEmpty 404 ] ]

                  assertThat (keysOf (at doc [ "paths"; "/widget"; "get"; "responses"; "404" ])) (isEqualTo [ "description" ])
          ) ]
    )

// ---------------------------------
// Serving
// ---------------------------------

let private servingTests =
    testList (
        "withDocs",
        [ testAsync (
              "serves the document at /openapi.json",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/openapi.json")

                          let app =
                              [ Endpoints.GET [ Endpoints.route "/ping" (text "pong") ] ]
                              |> OpenApi.withDocs info
                              |> Endpoints.toHandler

                          let! result = app next testCtx

                          match result with
                          | None -> failwith "Expected the spec endpoint to answer"
                          | Some _ ->
                              let body = readBody () |> Encoding.UTF8.GetString
                              assertThat (body.Contains "\"openapi\"") isTrue
                              assertThat (body.Contains "/ping") isTrue
                      }
                  )
          )

          testAsync (
              "serves a documentation page at /docs",
              fun _ ->
                  toAsync (
                      task {
                          let testCtx, readBody = TestContext.create (path = "/docs")

                          let app =
                              [ Endpoints.GET [ Endpoints.route "/ping" (text "pong") ] ]
                              |> OpenApi.withDocs info
                              |> Endpoints.toHandler

                          let! result = app next testCtx

                          match result with
                          | None -> failwith "Expected the docs endpoint to answer"
                          | Some _ ->
                              let body = readBody () |> Encoding.UTF8.GetString
                              assertThat (body.Contains "/openapi.json") isTrue
                      }
                  )
          )

          // The document is built before the two endpoints are appended, so they cannot
          // appear in it — FastAPI's `include_in_schema=False`, for free.
          test (
              "the spec and docs endpoints are absent from the document they serve",
              fun _ ->
                  let endpoints =
                      [ Endpoints.GET [ Endpoints.route "/ping" (text "pong") ] ]
                      |> OpenApi.withDocs info

                  // Rebuilding over the *augmented* list is what would wrongly include them;
                  // what withDocs actually served was built from the original list.
                  let served =
                      OpenApi.buildDocument info [ Endpoints.GET [ Endpoints.route "/ping" (text "pong") ] ]

                  assertThat (keysOf (prop served "paths")) (isEqualTo [ "/ping" ])
                  assertThat (List.length endpoints) (isEqualTo 3)
          ) ]
    )

let tests =
    testList ("OpenApi", [ templateTests; documentTests; schemaTests; servingTests ])
