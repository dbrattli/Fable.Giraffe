(**
# MCP — reusable Model Context Protocol handling

Implements the transport-independent JSON-RPC request/response core used by an MCP
Streamable HTTP endpoint. Applications provide server metadata and tools, execute
the `CallTool` action, and feed the result back through `completeToolCall`.
*)

module Fable.Giraffe.Mcp

open System.Text
open Fable.Giraffe.Json

type ServerInfo = { Name: string; Version: string }

type Server =
    { Info: ServerInfo
      ProtocolVersions: string list }

type Tool =
    { Name: string
      Description: string
      InputSchemaJson: string }

type ToolCall = { Name: string; ArgumentsJson: string }

type ToolResult = { Content: string; IsError: bool }

[<RequireQualifiedAccess>]
module ToolResult =

    /// A successful text result for the common MCP tool case.
    let text (content: string) : ToolResult = { Content = content; IsError = false }

    /// A text result that reports an application-level tool error to the caller.
    let error (content: string) : ToolResult = { Content = content; IsError = true }

type RequestId = private RequestId of obj

type Action =
    | NoResponse
    | Respond of string
    | CallTool of RequestId * ToolCall

/// A typed tool registration with its protocol description and application executor.
/// The executor accepts raw arguments so heterogeneous definitions can share one list;
/// `Tools.define` restores the input type before invoking application code.
type ToolDefinition =
    private
        { ProtocolTool: Tool
          Execute: string -> Async<ToolResult> }

let private str (value: string) : obj = box value
let private boolValue (value: bool) : obj = box value
let private intValue (value: int) : obj = box value

let private response (id: obj) (field: string) (value: obj) : string =
    rawObject [ "jsonrpc", str "2.0"; "id", id; field, value ]
    |> rawToJson

/// Complete a request with an arbitrary JSON result. This is the extensibility
/// path for structured tool content and MCP methods implemented by an application.
let buildResult (RequestId id) (resultJson: string) : string =
    response id "result" (deserialize resultJson)

let buildErrorWithId (id: obj) (code: int) (message: string) : string =
    let error = rawObject [ "code", intValue code; "message", str message ]
    response id "error" error

let buildError (id: RequestId) (code: int) (message: string) : string =
    let (RequestId rawId) = id
    buildErrorWithId rawId code message

let buildProtocolError (code: int) (message: string) : string = buildErrorWithId rawNull code message

let private buildErrorWithData (id: obj) (code: int) (message: string) (data: obj) : string =
    let error =
        rawObject [ "code", intValue code; "message", str message; "data", data ]

    response id "error" error

let private tryProperty (key: string) (value: obj) : obj option =
    if rawContains key value then
        Some(rawGet key value)
    else
        None

let private tryStringProperty (key: string) (value: obj) : string option =
    match tryProperty key value with
    | Some field when rawIsString field -> Some(rawAsString field)
    | _ -> None

let private initializeResult (server: Server) (version: string) : obj =
    rawObject
        [ "protocolVersion", str version
          "capabilities", rawObject [ "tools", rawObject [] ]
          "serverInfo", rawObject [ "name", str server.Info.Name; "version", str server.Info.Version ] ]

let private toolJson (tool: Tool) : obj =
    rawObject
        [ "name", str tool.Name
          "description", str tool.Description
          "inputSchema", deserialize tool.InputSchemaJson ]

let private toolsResult (tools: Tool list) : obj =
    rawObject [ "tools", tools |> List.map toolJson |> rawArray ]

/// Parse and dispatch one JSON-RPC request. Batches are deliberately rejected because
/// MCP removed batching in protocol revision 2025-06-18.
let handleRequest (server: Server) (tools: Tool list) (body: string) : Action =
    let parsed =
        try
            Some(deserialize body)
        with _ ->
            None

    match parsed with
    | None -> Respond(buildProtocolError -32700 "Parse error")
    | Some json when rawIsArray json -> Respond(buildProtocolError -32600 "Batch requests are not supported")
    | Some json when not (rawIsMap json) -> Respond(buildProtocolError -32600 "Invalid Request")
    | Some json ->
        let id = tryProperty "id" json

        let validId =
            id
            |> Option.forall (fun value ->
                rawIsString value
                || rawIsNumber value
                || rawIsNull value)

        match validId, tryStringProperty "jsonrpc" json, tryStringProperty "method" json with
        | false, _, _ -> Respond(buildProtocolError -32600 "Invalid Request")
        | true, Some "2.0", Some methodName ->
            match id with
            | None -> NoResponse
            | Some rawId ->
                let requestId = RequestId rawId

                match methodName with
                | "initialize" ->
                    let initializeParams =
                        tryProperty "params" json
                        |> Option.filter rawIsMap

                    let requested =
                        initializeParams
                        |> Option.bind (tryStringProperty "protocolVersion")

                    let validClient =
                        initializeParams
                        |> Option.bind (tryProperty "clientInfo")
                        |> Option.exists (fun client ->
                            rawIsMap client
                            && tryStringProperty "name" client |> Option.isSome
                            && tryStringProperty "version" client
                               |> Option.isSome)

                    let validCapabilities =
                        initializeParams
                        |> Option.bind (tryProperty "capabilities")
                        |> Option.exists rawIsMap

                    match requested, validClient, validCapabilities with
                    | _, false, _
                    | _, _, false
                    | None, _, _ -> Respond(buildError requestId -32602 "Invalid params for initialize")
                    | Some version, true, true when List.contains version server.ProtocolVersions ->
                        Respond(response rawId "result" (initializeResult server version))
                    | Some version, true, true ->
                        let data =
                            rawObject
                                [ "supported",
                                  server.ProtocolVersions
                                  |> List.map str
                                  |> rawArray
                                  "requested", str version ]

                        Respond(buildErrorWithData rawId -32602 "Unsupported protocol version" data)
                | "ping" -> Respond(response rawId "result" (rawObject []))
                | "tools/list" -> Respond(response rawId "result" (toolsResult tools))
                | "tools/call" ->
                    match tryProperty "params" json with
                    | Some parameters when rawIsMap parameters ->
                        let arguments =
                            tryProperty "arguments" parameters
                            |> Option.defaultWith (fun () -> rawObject [])

                        match tryStringProperty "name" parameters with
                        | Some name when rawIsMap arguments ->
                            if
                                tools
                                |> List.exists (fun tool -> tool.Name = name)
                            then
                                CallTool(
                                    requestId,
                                    { Name = name
                                      ArgumentsJson = rawToJson arguments }
                                )
                            else
                                Respond(buildError requestId -32602 $"Unknown tool: %s{name}")
                        | _ -> Respond(buildError requestId -32602 "Invalid params for tools/call")
                    | _ -> Respond(buildError requestId -32602 "Invalid params for tools/call")
                | _ -> Respond(buildError requestId -32601 $"Method not found: %s{methodName}")
        | _ -> Respond(buildErrorWithId (id |> Option.defaultValue rawNull) -32600 "Invalid Request")

/// Complete a tool call with one text content block. Use `buildResult` when the
/// application needs images, audio, resource links, structured content or metadata.
let completeToolCall (id: RequestId) (result: ToolResult) : string =
    let content =
        rawArray [ rawObject [ "type", str "text"; "text", str result.Content ] ]

    let fields =
        if result.IsError then
            [ "content", content; "isError", boolValue true ]
        else
            [ "content", content ]

    buildResult id (rawObject fields |> rawToJson)

/// The POST/JSON subset of Streamable HTTP for an asynchronous MCP dispatcher.
/// `None` represents an accepted notification; `Some json` is a JSON-RPC response.
let streamableHttpAsync (dispatch: string -> Async<string option>) : HttpHandler =
    fun (_: HttpFunc) (ctx: HttpContext) ->
        task {
            let! body = ctx.ReadBodyFromRequestAsync()
            let! result = dispatch body |> startAsTask

            match result with
            | None ->
                ctx.SetStatusCode 202
                return! ctx.WriteBytesAsync [||]
            | Some json ->
                ctx.SetStatusCode 200
                ctx.SetContentType "application/json"
                return! ctx.WriteBytesAsync(Encoding.UTF8.GetBytes json)
        }

/// Explicit, typed MCP tool registration. This is the Fable-portable counterpart
/// to reflection/attribute-based registration in runtime-specific MCP SDKs.
module Tools =

    /// Compiler-facing constructor used by the inline typed builders.
    [<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
    let defineCore (name: string) (inputSchemaJson: string) (execute: string -> Async<ToolResult>) : ToolDefinition =
        { ProtocolTool =
            { Name = name
              Description = ""
              InputSchemaJson = inputSchemaJson }
          Execute = execute }

    /// Define an asynchronous tool. Its input schema and decoder come from the same
    /// TypedJson type walk; note that TypedJson may coerce compatible primitives.
    let inline define<'Input> (name: string) (execute: 'Input -> Async<ToolResult>) : ToolDefinition =
        let invoke argumentsJson =
            async {
                match tryDeserialize<'Input> argumentsJson with
                | Ok arguments -> return! execute arguments
                | Error _ -> return ToolResult.error "Invalid tool arguments"
            }

        defineCore name (schemaFor typeof<'Input>) invoke

    /// Define a synchronous tool without manually wrapping its result in `async`.
    let inline defineSync<'Input> (name: string) (execute: 'Input -> ToolResult) : ToolDefinition = define name (execute >> async.Return)

    /// Define a synchronous typed tool. This is the concise application-facing
    /// alias for `defineSync`.
    let inline tool<'Input> (name: string) (execute: 'Input -> ToolResult) : ToolDefinition = defineSync name execute

    /// Define an asynchronous typed tool. This is the concise application-facing
    /// alias for `define`.
    let inline toolAsync<'Input> (name: string) (execute: 'Input -> Async<ToolResult>) : ToolDefinition = define name execute

    /// Set the human-readable description returned by `tools/list`.
    let description (text: string) (definition: ToolDefinition) : ToolDefinition =
        { definition with
            ProtocolTool =
                { definition.ProtocolTool with
                    Description = text } }

    /// Set the human-readable description returned by `tools/list`.
    let describe (text: string) (definition: ToolDefinition) : ToolDefinition = description text definition

    /// The protocol descriptions consumed by the transport-independent MCP core.
    let protocolTools (definitions: ToolDefinition list) : Tool list = definitions |> List.map _.ProtocolTool

    let private validate (definitions: ToolDefinition list) =
        let duplicate =
            definitions
            |> List.groupBy _.ProtocolTool.Name
            |> List.tryFind (fun (_, definitions) -> List.length definitions > 1)

        match duplicate with
        | Some(name, _) -> invalidArg "definitions" $"Duplicate MCP tool name: %s{name}"
        | None -> ()

    /// Build an asynchronous dispatcher once at composition time. Protocol parsing
    /// remains in `handleRequest`; only a `CallTool` action enters application code.
    let dispatcher (server: Server) (definitions: ToolDefinition list) : string -> Async<string option> =
        validate definitions
        let tools = protocolTools definitions

        fun body ->
            async {
                match handleRequest server tools body with
                | NoResponse -> return None
                | Respond response -> return Some response
                | CallTool(id, call) ->
                    let definition =
                        definitions
                        |> List.find (fun definition -> definition.ProtocolTool.Name = call.Name)

                    let! outcome =
                        definition.Execute call.ArgumentsJson
                        |> Async.Catch

                    match outcome with
                    | Choice1Of2 result -> return Some(completeToolCall id result)
                    | Choice2Of2 _ -> return Some(buildError id -32603 "Internal error")
            }

    /// Mount typed tools as the synchronous-response subset of Streamable HTTP.
    /// Authentication, transport headers, sessions and timeouts remain composable policy.
    let host (server: Server) (definitions: ToolDefinition list) : HttpHandler =
        dispatcher server definitions
        |> streamableHttpAsync

    /// Create a classic Giraffe handler for the typed tools.
    let handler (server: Server) (definitions: ToolDefinition list) : HttpHandler = host server definitions

/// The POST/JSON subset of Streamable HTTP for a synchronous MCP dispatcher.
/// An empty result represents an accepted notification and is returned as HTTP 202.
/// Authentication, Origin/Accept/version-header validation, sessions, GET/SSE and
/// timeouts remain transport/application policy and can be composed around this handler.
let streamableHttp (dispatch: string -> string) : HttpHandler =
    fun (_: HttpFunc) (ctx: HttpContext) ->
        task {
            let! body = ctx.ReadBodyFromRequestAsync()
            let result = dispatch body

            if result = "" then
                ctx.SetStatusCode 202
                return! ctx.WriteBytesAsync [||]
            else
                ctx.SetStatusCode 200
                ctx.SetContentType "application/json"
                return! ctx.WriteBytesAsync(Encoding.UTF8.GetBytes result)
        }
