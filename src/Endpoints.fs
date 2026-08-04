namespace Fable.Giraffe

open System

open Fable.Giraffe.FormatExpressions

/// <summary>
/// The HTTP verb an <see cref="Endpoint"/> answers.
///
/// <c>RequireQualifiedAccess</c> is deliberate: the case names would otherwise shadow the
/// <see cref="HttpHandler"/> verb filters of the same name in <c>Core</c> for anyone doing
/// <c>open Fable.Giraffe</c>, silently breaking classic <c>GET &gt;=&gt; route "/x"</c> composition.
/// </summary>
[<RequireQualifiedAccess>]
type HttpVerb =
    | GET
    | POST
    | PUT
    | PATCH
    | DELETE
    | HEAD
    | OPTIONS
    | TRACE
    | CONNECT
    /// Answers any verb. Routes fine, but cannot be described in an OpenAPI document,
    /// which requires a concrete method per operation.
    | ANY

/// <summary>
/// Descriptive metadata attached to an <see cref="Endpoint"/>. Carries no behaviour — it exists
/// so that an API description can be produced without inspecting the composed
/// <see cref="HttpHandler"/>, which is a bare closure and therefore opaque.
/// </summary>
type EndpointMeta =
    {
        Summary: string option
        Description: string option
        Tags: string list
        OperationId: string option
        Deprecated: bool
        /// Names for the route template's format parameters, in order. Empty means auto-name
        /// positionally (<c>p0</c>, <c>p1</c>, …).
        PathParamNames: string list
        /// Request body type and its content type.
        Accepts: (Type * string) option
        /// Status code, body type (<c>None</c> for an empty body) and content type.
        Responses: (int * Type option * string) list
    }

    static member Default =
        { Summary = None
          Description = None
          Tags = []
          OperationId = None
          Deprecated = false
          PathParamNames = []
          Accepts = None
          Responses = [] }

/// <summary>
/// A reified description of a route: verb, path, metadata and the handler that serves it.
///
/// This exists because <c>HttpHandler = HttpFunc -&gt; HttpFunc</c> is a bare closure — <c>route "/ping"</c>
/// erases <c>"/ping"</c> and <c>choose</c> collapses its list at composition time, so a composed
/// application carries no description of itself. An <c>Endpoint list</c> is that description, and
/// <c>Endpoints.toHandler</c> lowers it back onto the ordinary routing combinators.
/// </summary>
[<RequireQualifiedAccess>]
type Endpoint =
    /// A literal path, e.g. <c>/ping</c>.
    | Simple of verb: HttpVerb * path: string * meta: EndpointMeta * handler: HttpHandler
    /// A printf-style template, e.g. <c>/user/%i</c>. The handler already closes over the
    /// <c>routef</c> application; only the template and its format characters are retained,
    /// because Fable erases generics and re-opening <c>'T</c> would need an unsafe cast.
    | Template of verb: HttpVerb * template: string * formatChars: char list * meta: EndpointMeta * handler: HttpHandler
    /// A shared path prefix. Metadata set here is inherited by every child.
    | Nested of prefix: string * meta: EndpointMeta * endpoints: Endpoint list
    /// A group with no path or metadata of its own.
    | Multi of endpoints: Endpoint list

/// <summary>
/// An opt-in, describable routing layer, sitting alongside classic <c>choose [ ... ]</c> composition
/// rather than replacing it — the same way upstream Giraffe carries <c>EndpointRouting</c> next to
/// classic routing.
///
/// <c>open Fable.Giraffe.Endpoints</c> to use the DSL; the <c>route</c>, <c>routef</c>, <c>subRoute</c>
/// and verb functions here deliberately shadow their <see cref="HttpHandler"/>-returning namesakes.
/// </summary>
/// <example>
/// <code>
/// open Fable.Giraffe
/// open Fable.Giraffe.Endpoints
///
/// let endpoints = [
///     GET [
///         route "/ping" (text "pong")
///         |> summary "Health check"
///         |> responds&lt;string&gt; 200
///
///         routef "/user/%i" getUser
///         |> pathParams [ "id" ]
///         |> responds&lt;User&gt; 200
///         |> respondsEmpty 404
///     ]
///     POST [ route "/user" createUser |> accepts&lt;NewUser&gt; |> responds&lt;User&gt; 201 ]
/// ]
///
/// app.UseGiraffe(Endpoints.toHandler endpoints)
/// </code>
/// </example>
module Endpoints =

    // ---------------------------------
    // Metadata
    // ---------------------------------

    /// <summary>
    /// Applies a metadata transform to an endpoint, recursing into groups so that a transform
    /// applied to <c>GET [ a; b ]</c> reaches both leaves, and one applied to a
    /// <see cref="Endpoint.Nested"/> is inherited by its children.
    ///
    /// This is the composition point every metadata combinator is built from — the analogue of
    /// Giraffe's <c>ConfigureEndpoint</c>, with a plain record accumulator in place of ASP.NET's
    /// <c>IEndpointConventionBuilder</c>.
    /// </summary>
    let rec configureEndpoint (f: EndpointMeta -> EndpointMeta) (endpoint: Endpoint) : Endpoint =
        match endpoint with
        | Endpoint.Simple(v, path, meta, handler) -> Endpoint.Simple(v, path, f meta, handler)
        | Endpoint.Template(v, template, chars, meta, handler) -> Endpoint.Template(v, template, chars, f meta, handler)
        | Endpoint.Nested(prefix, meta, endpoints) -> Endpoint.Nested(prefix, f meta, endpoints)
        | Endpoint.Multi endpoints -> Endpoint.Multi(endpoints |> List.map (configureEndpoint f))

    /// Sets a one-line summary of what the endpoint does.
    let summary (text: string) =
        configureEndpoint (fun m -> { m with Summary = Some text })

    /// Sets a longer description of the endpoint.
    let description (text: string) =
        configureEndpoint (fun m -> { m with Description = Some text })

    /// Adds tags used to group operations in the generated documentation.
    let tags (values: string list) =
        configureEndpoint (fun m -> { m with Tags = m.Tags @ values })

    /// Sets an explicit operation id. Defaults to one derived from the verb and path.
    let operationId (id: string) =
        configureEndpoint (fun m -> { m with OperationId = Some id })

    /// Marks the endpoint as deprecated.
    let deprecated (endpoint: Endpoint) =
        configureEndpoint (fun m -> { m with Deprecated = true }) endpoint

    /// <summary>
    /// Names the route template's parameters, in the order they appear. Without this they are
    /// named positionally (<c>p0</c>, <c>p1</c>, …), since <c>routef</c> templates carry no names.
    /// </summary>
    let pathParams (names: string list) =
        configureEndpoint (fun m -> { m with PathParamNames = names })

    /// <summary>
    /// Declares the request body type. Must be <c>inline</c> so that <c>typeof&lt;'T&gt;</c> is resolved
    /// at the call site — Fable erases generics, so a non-inline version would capture nothing.
    /// </summary>
    let inline accepts<'T> (endpoint: Endpoint) : Endpoint =
        configureEndpoint
            (fun m ->
                { m with
                    Accepts = Some(typeof<'T>, "application/json") })
            endpoint

    /// Declares the request body type with an explicit content type.
    let inline acceptsWith<'T> (contentType: string) (endpoint: Endpoint) : Endpoint =
        configureEndpoint
            (fun m ->
                { m with
                    Accepts = Some(typeof<'T>, contentType) })
            endpoint

    /// <summary>
    /// Declares a response body type for a status code. <c>inline</c> for the same reason as
    /// <see cref="accepts"/>.
    /// </summary>
    let inline responds<'T> (statusCode: int) (endpoint: Endpoint) : Endpoint =
        configureEndpoint
            (fun m ->
                { m with
                    Responses =
                        m.Responses
                        @ [ statusCode, Some typeof<'T>, "application/json" ] })
            endpoint

    /// Declares a response body type for a status code, with an explicit content type.
    let inline respondsWith<'T> (statusCode: int) (contentType: string) (endpoint: Endpoint) : Endpoint =
        configureEndpoint
            (fun m ->
                { m with
                    Responses =
                        m.Responses
                        @ [ statusCode, Some typeof<'T>, contentType ] })
            endpoint

    /// Declares a response with no body, e.g. a 404 or 204.
    let respondsEmpty (statusCode: int) =
        configureEndpoint (fun m ->
            { m with
                Responses = m.Responses @ [ statusCode, None, "" ] })

    // ---------------------------------
    // Routes
    // ---------------------------------

    /// <summary>
    /// Describes a route with a literal path. Shadows <c>Routing.route</c>, which returns an
    /// <see cref="HttpHandler"/> rather than an <see cref="Endpoint"/>.
    /// </summary>
    let route (path: string) (handler: HttpHandler) : Endpoint =
        Endpoint.Simple(HttpVerb.ANY, path, EndpointMeta.Default, handler)

    /// <summary>
    /// Describes a route with a printf-style template, e.g. <c>routef "/user/%i" getUser</c>.
    ///
    /// The template is applied to <c>Routing.routef</c> here, at the call site, so <c>'T</c> is closed
    /// before it can be erased; the template string and its format characters are kept alongside so
    /// the route can still be described. Throws at composition time on an unsupported format
    /// character, which is the right place to fail.
    /// </summary>
    let routef (path: PrintfFormat<_, _, _, _, 'T>) (routeHandler: 'T -> HttpHandler) : Endpoint =
        Endpoint.Template(HttpVerb.ANY, path.Value, getFormatChars path.Value, EndpointMeta.Default, Routing.routef path routeHandler)

    /// Groups endpoints under a shared path prefix. Metadata applied to the group is inherited.
    let subRoute (prefix: string) (endpoints: Endpoint list) : Endpoint =
        Endpoint.Nested(prefix, EndpointMeta.Default, endpoints)

    // ---------------------------------
    // Verbs
    // ---------------------------------

    let rec private applyVerb (verb: HttpVerb) (endpoint: Endpoint) : Endpoint =
        match endpoint with
        | Endpoint.Simple(_, path, meta, handler) -> Endpoint.Simple(verb, path, meta, handler)
        | Endpoint.Template(_, template, chars, meta, handler) -> Endpoint.Template(verb, template, chars, meta, handler)
        | Endpoint.Nested(prefix, meta, endpoints) -> Endpoint.Nested(prefix, meta, endpoints |> List.map (applyVerb verb))
        | Endpoint.Multi endpoints -> Endpoint.Multi(endpoints |> List.map (applyVerb verb))

    let private group (verb: HttpVerb) (endpoints: Endpoint list) : Endpoint =
        Endpoint.Multi(endpoints |> List.map (applyVerb verb))

    let GET (endpoints: Endpoint list) = group HttpVerb.GET endpoints
    let POST (endpoints: Endpoint list) = group HttpVerb.POST endpoints
    let PUT (endpoints: Endpoint list) = group HttpVerb.PUT endpoints
    let PATCH (endpoints: Endpoint list) = group HttpVerb.PATCH endpoints
    let DELETE (endpoints: Endpoint list) = group HttpVerb.DELETE endpoints
    let HEAD (endpoints: Endpoint list) = group HttpVerb.HEAD endpoints
    let OPTIONS (endpoints: Endpoint list) = group HttpVerb.OPTIONS endpoints
    let TRACE (endpoints: Endpoint list) = group HttpVerb.TRACE endpoints
    let CONNECT (endpoints: Endpoint list) = group HttpVerb.CONNECT endpoints

    // ---------------------------------
    // Lowering
    // ---------------------------------

    /// <summary>
    /// The <see cref="HttpHandler"/> verb filter for a verb, or <c>None</c> for
    /// <see cref="HttpVerb.ANY"/> — returning an option rather than an identity handler keeps
    /// <c>compose</c> out of the request path when no filtering is wanted.
    /// </summary>
    let private verbFilter (verb: HttpVerb) : HttpHandler option =
        match verb with
        | HttpVerb.GET -> Some Core.GET
        | HttpVerb.POST -> Some Core.POST
        | HttpVerb.PUT -> Some Core.PUT
        | HttpVerb.PATCH -> Some Core.PATCH
        | HttpVerb.DELETE -> Some Core.DELETE
        | HttpVerb.HEAD -> Some Core.HEAD
        | HttpVerb.OPTIONS -> Some Core.OPTIONS
        | HttpVerb.TRACE -> Some Core.TRACE
        | HttpVerb.CONNECT -> Some Core.CONNECT
        | HttpVerb.ANY -> None

    let private withVerb (verb: HttpVerb) (handler: HttpHandler) : HttpHandler =
        match verbFilter verb with
        | Some filter -> filter >=> handler
        | None -> handler

    let rec private lower (endpoint: Endpoint) : HttpHandler =
        match endpoint with
        | Endpoint.Simple(verb, path, _, handler) -> withVerb verb (Routing.route path >=> handler)
        // The handler already wraps `Routing.routef`, which does its own path matching.
        | Endpoint.Template(verb, _, _, _, handler) -> withVerb verb handler
        | Endpoint.Nested(prefix, _, endpoints) -> Routing.subRoute prefix (choose (endpoints |> List.map lower))
        | Endpoint.Multi endpoints -> choose (endpoints |> List.map lower)

    /// <summary>
    /// Lowers an endpoint list onto the ordinary routing combinators — <c>route</c>, <c>routef</c>,
    /// <c>subRoute</c> and <c>choose</c>. No routing semantics are reimplemented here, so there
    /// remains exactly one path matcher in the framework and the existing suite keeps guarding it.
    /// </summary>
    let toHandler (endpoints: Endpoint list) : HttpHandler = choose (endpoints |> List.map lower)
