namespace Fable.Giraffe

open System

open Fable.TypedJson.Schema

open Fable.Giraffe.Json

/// Document-level metadata for the generated OpenAPI description.
type OpenApiInfo =
    { Title: string
      Version: string
      Description: string option }

    static member Create(title: string, version: string) =
        { Title = title
          Version = version
          Description = None }

/// <summary>
/// Generates an OpenAPI 3.1 document from an <see cref="Endpoint"/> list, and serves it.
///
/// The document is a pure function of the endpoint list, the way FastAPI's
/// <c>get_openapi(routes=...)</c> is a pure function of <c>app.routes</c>. Nothing is
/// recovered from a composed <see cref="HttpHandler"/> — that is impossible, which is why
/// the <see cref="Endpoint"/> layer exists.
///
/// Schemas come from Fable.TypedJson, off the same walk that builds the serializer's codec.
/// The document therefore cannot describe a property the wire does not carry.
/// </summary>
module OpenApi =

    [<Literal>]
    let private SchemaRef = "#/components/schemas/"

    // ---------------------------------
    // Path templates and parameters
    // ---------------------------------

    /// <summary>
    /// The OpenAPI <c>type</c>/<c>format</c> pair for a route format character. Mirrors
    /// <c>FormatExpressions.formatStringMap</c>, which owns the matching side of the same
    /// characters; that table maps to regexes and parsers and carries no type information,
    /// so the mapping is stated once here rather than widened there.
    /// </summary>
    let private paramSchema (c: char) : JsonSchemaValue =
        let node t fmt =
            match fmt with
            | Some f -> SVDict(Map.ofList [ "type", SVStr t; "format", SVStr f ])
            | None -> SVDict(Map.ofList [ "type", SVStr t ])

        match c with
        | 'b' -> node "boolean" None
        | 'c' -> SVDict(Map.ofList [ "type", SVStr "string"; "maxLength", SVInt 1 ])
        | 's' -> node "string" None
        | 'i' -> node "integer" (Some "int32")
        | 'd' -> node "integer" (Some "int64")
        | 'f' -> node "number" (Some "double")
        | 'O' -> node "string" (Some "uuid")
        | 'u' -> node "string" None
        | _ -> node "string" None

    /// The declared name for the parameter at <paramref name="index"/>, or a positional
    /// fallback. <c>routef</c> templates carry no names — <c>Endpoints.pathParams</c> supplies
    /// them.
    let private paramName (names: string list) (index: int) : string =
        if index < List.length names then
            names[index]
        else
            $"p%d{index}"

    /// <summary>
    /// Rewrites a printf-style route template into an OpenAPI path template:
    /// <c>"/user/%i"</c> with names <c>[ "id" ]</c> becomes <c>"/user/{id}"</c>.
    /// An escaped <c>%%</c> is a literal percent sign and consumes no name.
    /// </summary>
    let toPathTemplate (names: string list) (template: string) : string =
        let rec convert (chars: char list) (index: int) =
            match chars with
            | '%' :: '%' :: tail ->
                let rest = convert tail index
                "%" + rest
            | '%' :: _ :: tail ->
                let rest = convert tail (index + 1)
                "{" + paramName names index + "}" + rest
            | c :: tail -> string c + convert tail index
            | [] -> ""

        convert (List.ofSeq template) 0

    // ---------------------------------
    // Flattening the endpoint tree
    // ---------------------------------

    /// One row of the route table: everything the assembler needs about a single operation.
    type private Operation =
        { Verb: HttpVerb
          Path: string
          FormatChars: char list
          Meta: EndpointMeta }

    /// Metadata set on a group flows down to its leaves. Tags accumulate; a summary or
    /// description set on the leaf wins over one inherited from the group.
    let private inherits (parent: EndpointMeta) (child: EndpointMeta) : EndpointMeta =
        { child with
            Tags = parent.Tags @ child.Tags
            Summary = Option.orElse parent.Summary child.Summary
            Description = Option.orElse parent.Description child.Description }

    let rec private flatten (prefix: string) (inherited: EndpointMeta) (endpoint: Endpoint) : Operation list =
        match endpoint with
        | Endpoint.Simple(verb, path, meta, _) ->
            [ { Verb = verb
                Path = prefix + path
                FormatChars = []
                Meta = inherits inherited meta } ]
        | Endpoint.Template(verb, template, chars, meta, _) ->
            let meta = inherits inherited meta

            [ { Verb = verb
                Path =
                  prefix
                  + toPathTemplate meta.PathParamNames template
                FormatChars = chars
                Meta = meta } ]
        | Endpoint.Nested(nestedPrefix, meta, endpoints) ->
            endpoints
            |> List.collect (flatten (prefix + nestedPrefix) (inherits inherited meta))
        | Endpoint.Multi endpoints ->
            endpoints
            |> List.collect (flatten prefix inherited)

    // ---------------------------------
    // Operations
    // ---------------------------------

    let private verbKey (verb: HttpVerb) : string option =
        match verb with
        | HttpVerb.GET -> Some "get"
        | HttpVerb.POST -> Some "post"
        | HttpVerb.PUT -> Some "put"
        | HttpVerb.PATCH -> Some "patch"
        | HttpVerb.DELETE -> Some "delete"
        | HttpVerb.HEAD -> Some "head"
        | HttpVerb.OPTIONS -> Some "options"
        | HttpVerb.TRACE -> Some "trace"
        | HttpVerb.CONNECT -> Some "connect"
        // OpenAPI requires a concrete method per operation, so a route that answers any
        // verb cannot be described. It still routes; it is simply absent from the document.
        | HttpVerb.ANY -> None

    /// Every type an operation mentions, in declaration order.
    let private referencedTypes (meta: EndpointMeta) : Type list =
        let fromAccepts = meta.Accepts |> Option.map fst |> Option.toList

        let fromResponses = meta.Responses |> List.choose (fun (_, t, _) -> t)

        fromAccepts @ fromResponses

    let private mediaType (contentType: string) (schema: JsonSchemaValue) =
        SVDict(Map.ofList [ contentType, SVDict(Map.ofList [ "schema", schema ]) ])

    let private buildParameters (op: Operation) : JsonSchemaValue list =
        op.FormatChars
        |> List.mapi (fun i c ->
            SVDict(
                Map.ofList
                    [ "name", SVStr(paramName op.Meta.PathParamNames i)
                      "in", SVStr "path"
                      // A path parameter is always required — the route does not match without it.
                      "required", SVBool true
                      "schema", paramSchema c ]
            ))

    /// The default when nothing is declared. An operation with no `responses` is invalid
    /// OpenAPI, so something has to be emitted.
    let private defaultResponses =
        SVDict(Map.ofList [ "200", SVDict(Map.ofList [ "description", SVStr "Success" ]) ])

    let private buildResponses (schemaOf: Type -> JsonSchemaValue) (meta: EndpointMeta) : JsonSchemaValue =
        match meta.Responses with
        | [] -> defaultResponses
        | responses ->
            responses
            |> List.map (fun (code, bodyType, contentType) ->
                let body =
                    match bodyType with
                    | Some t ->
                        Map.ofList
                            [ "description", SVStr "Success"
                              "content", mediaType contentType (schemaOf t) ]
                    | None -> Map.ofList [ "description", SVStr "No content" ]

                string code, SVDict body)
            |> Map.ofList
            |> SVDict

    let private buildOperation (schemaOf: Type -> JsonSchemaValue) (op: Operation) (verb: string) : JsonSchemaValue =
        let meta = op.Meta

        let baseFields =
            [ yield "responses", buildResponses schemaOf meta

              match meta.Summary with
              | Some s -> yield "summary", SVStr s
              | None -> ()

              match meta.Description with
              | Some d -> yield "description", SVStr d
              | None -> ()

              if not (List.isEmpty meta.Tags) then
                  yield "tags", SVList(meta.Tags |> List.map SVStr)

              // Falls back to a stable id derived from the verb and path, the way FastAPI
              // derives one from the endpoint function when none is given.
              yield
                  "operationId",
                  SVStr(
                      match meta.OperationId with
                      | Some id -> id
                      | None ->
                          verb
                          + op.Path.Replace("/", "_").Replace("{", "").Replace("}", "")
                  )

              if meta.Deprecated then
                  yield "deprecated", SVBool true

              let parameters = buildParameters op

              if not (List.isEmpty parameters) then
                  yield "parameters", SVList parameters

              match meta.Accepts with
              | Some(t, contentType) ->
                  yield "requestBody", SVDict(Map.ofList [ "required", SVBool true; "content", mediaType contentType (schemaOf t) ])
              | None -> () ]

        SVDict(Map.ofList baseFields)

    // ---------------------------------
    // Document
    // ---------------------------------

    /// <summary>
    /// Builds the OpenAPI document for an endpoint list.
    ///
    /// Two passes, as FastAPI's <c>get_openapi</c> does: every referenced type is resolved
    /// first so each schema lands once in <c>components/schemas</c>, then operations
    /// reference them by <c>$ref</c>. Emitting them inline per operation would repeat a
    /// shared type once per mention and could not represent a recursive one at all.
    /// </summary>
    let buildDocument (info: OpenApiInfo) (endpoints: Endpoint list) : JsonSchemaValue =
        let operations =
            endpoints
            |> List.collect (flatten "" EndpointMeta.Default)
            |> List.filter (fun op -> (verbKey op.Verb).IsSome)

        // Pass one: resolve every referenced type, collecting the definitions each drags in.
        let roots, definitions =
            operations
            |> List.collect (fun op -> referencedTypes op.Meta)
            |> List.fold
                (fun (roots: Map<string, JsonSchemaValue>, defs: Map<string, JsonSchemaValue>) t ->
                    let root, typeDefs = schemaWithDefsFor SchemaRef t
                    let roots = Map.add t.FullName root roots

                    let defs =
                        typeDefs
                        |> Map.fold (fun acc k v -> Map.add k v acc) defs

                    roots, defs)
                (Map.empty, Map.empty)

        let schemaOf (t: Type) =
            match Map.tryFind t.FullName roots with
            | Some schema -> schema
            | None -> SVDict Map.empty

        // Pass two: group operations by path, so two verbs on one path share a path item.
        let paths =
            operations
            |> List.fold
                (fun (acc: Map<string, Map<string, JsonSchemaValue>>) op ->
                    match verbKey op.Verb with
                    | None -> acc
                    | Some verb ->
                        let item =
                            acc
                            |> Map.tryFind op.Path
                            |> Option.defaultValue Map.empty

                        Map.add op.Path (Map.add verb (buildOperation schemaOf op verb) item) acc)
                Map.empty
            |> Map.map (fun _ item -> SVDict item)

        let infoFields =
            [ yield "title", SVStr info.Title
              yield "version", SVStr info.Version

              match info.Description with
              | Some d -> yield "description", SVStr d
              | None -> () ]

        SVDict(
            Map.ofList
                [ "openapi", SVStr "3.1.0"
                  "info", SVDict(Map.ofList infoFields)
                  "paths", SVDict paths
                  "components", SVDict(Map.ofList [ "schemas", SVDict definitions ]) ]
        )

    // ---------------------------------
    // Serving
    // ---------------------------------

    /// <summary>
    /// The documentation UI, as a single HTML page pointing at the spec URL. Scalar is loaded
    /// from a CDN — the same shape as FastAPI's <c>/docs</c>, which is a Swagger UI script tag
    /// and nothing more.
    /// </summary>
    let private docsHtml (title: string) (specUrl: string) =
        $"""<!doctype html>
<html>
  <head>
    <title>%s{title}</title>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
  </head>
  <body>
    <script id="api-reference" data-url="%s{specUrl}"></script>
    <script src="https://cdn.jsdelivr.net/npm/@scalar/api-reference"></script>
  </body>
</html>"""

    /// <summary>
    /// Appends a spec endpoint and a documentation UI to an endpoint list.
    /// </summary>
    /// <remarks>
    /// The document is built <b>once, here</b>, at composition time, and the two endpoints
    /// close over the resulting string. That is FastAPI's <c>app.openapi_schema</c>
    /// memoization done eagerly, and it matters on BEAM specifically: an immutable string
    /// crosses Cowboy's per-request process boundary, where a lazily-populated cache could not.
    ///
    /// The two added endpoints are absent from the document they serve, because the document
    /// is built before they are appended — FastAPI's <c>include_in_schema=False</c>, for free.
    /// </remarks>
    /// <example>
    /// <code>
    /// let webApp =
    ///     endpoints
    ///     |> OpenApi.withDocs (OpenApiInfo.Create("My API", "1.0"))
    ///     |> Endpoints.toHandler
    /// </code>
    /// </example>
    let withDocsAt (specUrl: string) (docsUrl: string) (info: OpenApiInfo) (endpoints: Endpoint list) : Endpoint list =
        let document = buildDocument info endpoints |> renderJson
        let html = docsHtml info.Title specUrl

        let specHandler =
            setContentType "application/json; charset=utf-8"
            >=> setBodyFromString document

        let docsHandler =
            setContentType "text/html; charset=utf-8"
            >=> setBodyFromString html

        endpoints
        @ [ Endpoint.Simple(HttpVerb.GET, specUrl, EndpointMeta.Default, specHandler)
            Endpoint.Simple(HttpVerb.GET, docsUrl, EndpointMeta.Default, docsHandler) ]

    /// Serves the spec at <c>/openapi.json</c> and the documentation UI at <c>/docs</c>.
    let withDocs (info: OpenApiInfo) (endpoints: Endpoint list) : Endpoint list =
        withDocsAt "/openapi.json" "/docs" info endpoints
