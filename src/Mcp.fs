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

type RequestId = private RequestId of obj

type Action =
    | NoResponse
    | Respond of string
    | CallTool of RequestId * ToolCall

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
