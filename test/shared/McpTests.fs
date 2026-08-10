module Fable.Giraffe.Tests.McpTests

open System.Text
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

open Fable.Giraffe
open Fable.Giraffe.Json
open Fable.Giraffe.Mcp

let private server =
    { Info =
        { Name = "test-server"
          Version = "1.2.3" }
      ProtocolVersions = [ "2025-03-26"; "2025-11-25" ] }

let private tools =
    [ { Name = "echo"
        Description = "Echo text"
        InputSchemaJson = """{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}""" } ]

let private responseJson =
    function
    | Respond json -> json
    | other -> failwith $"Expected a response, got %A{other}"

let private field key json = json |> deserialize |> rawGet key

let private errorCode json =
    json
    |> responseJson
    |> field "error"
    |> rawGet "code"
    |> rawToJson

let private responseId json =
    json |> responseJson |> field "id" |> rawToJson

let tests =
    testList (
        "MCP",
        [ test (
              "initialize negotiates a supported protocol version",
              fun _ ->
                  let json =
                      handleRequest
                          server
                          tools
                          """{"jsonrpc":"2.0","id":"a","method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test-client","version":"1.0"}}}"""
                      |> responseJson

                  let result = field "result" json
                  assertThat (result |> rawGet "protocolVersion" |> rawAsString) (isEqualTo "2025-03-26")

                  assertThat
                      (result
                       |> rawGet "serverInfo"
                       |> rawGet "name"
                       |> rawAsString)
                      (isEqualTo "test-server")
          )

          test (
              "tools/call returns an application action and preserves arguments",
              fun _ ->
                  match
                      handleRequest
                          server
                          tools
                          """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"echo","arguments":{"text":"hello"}}}"""
                  with
                  | CallTool(id, call) ->
                      assertThat call.Name (isEqualTo "echo")

                      assertThat
                          (call.ArgumentsJson
                           |> deserialize
                           |> rawGet "text"
                           |> rawAsString)
                          (isEqualTo "hello")

                      let response = completeToolCall id { Content = "hello"; IsError = false }
                      let result = field "result" response
                      assertThat (result |> rawGet "content" |> rawIsArray) isTrue
                  | other -> failwith $"Expected a tool call, got %A{other}"
          )

          test (
              "tools/call defaults omitted arguments to an empty object",
              fun _ ->
                  match handleRequest server tools """{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"echo"}}""" with
                  | CallTool(_, call) -> assertThat call.ArgumentsJson (isEqualTo (rawObject [] |> rawToJson))
                  | other -> failwith $"Expected a tool call, got %A{other}"
          )

          test (
              "string numeric and null IDs are preserved",
              fun _ ->
                  assertThat
                      (handleRequest server tools """{"jsonrpc":"2.0","id":"a\"b","method":"ping"}"""
                       |> responseId)
                      (isEqualTo "\"a\\\"b\"")

                  assertThat
                      (handleRequest server tools """{"jsonrpc":"2.0","id":42,"method":"ping"}"""
                       |> responseId)
                      (isEqualTo "42")

                  assertThat
                      (handleRequest server tools """{"jsonrpc":"2.0","id":null,"method":"ping"}"""
                       |> responseId)
                      (isEqualTo "null")
          )

          test (
              "structured IDs are rejected instead of echoed",
              fun _ ->
                  let response =
                      handleRequest server tools """{"jsonrpc":"2.0","id":{"unsafe":true},"method":"ping"}"""

                  assertThat (errorCode response) (isEqualTo "-32600")
                  assertThat (responseId response) (isEqualTo "null")
          )

          test ("malformed JSON is a parse error", fun _ -> assertThat (handleRequest server tools "{" |> errorCode) (isEqualTo "-32700"))

          test (
              "invalid request members produce Invalid Request",
              fun _ ->
                  assertThat
                      (handleRequest server tools """{"jsonrpc":"1.0","id":1,"method":"ping"}"""
                       |> errorCode)
                      (isEqualTo "-32600")

                  assertThat
                      (handleRequest server tools """{"jsonrpc":"2.0","id":1,"method":7}"""
                       |> errorCode)
                      (isEqualTo "-32600")
          )

          test (
              "unsupported protocol versions report negotiation data",
              fun _ ->
                  let json =
                      handleRequest
                          server
                          tools
                          """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2099-01-01","capabilities":{},"clientInfo":{"name":"test-client","version":"1.0"}}}"""
                      |> responseJson

                  assertThat
                      (json
                       |> field "error"
                       |> rawGet "code"
                       |> rawToJson)
                      (isEqualTo "-32602")

                  assertThat
                      (json
                       |> field "error"
                       |> rawGet "data"
                       |> rawGet "requested"
                       |> rawAsString)
                      (isEqualTo "2099-01-01")
          )

          test (
              "missing initialize parameters are rejected",
              fun _ ->
                  assertThat
                      (handleRequest server tools """{"jsonrpc":"2.0","id":1,"method":"initialize"}"""
                       |> errorCode)
                      (isEqualTo "-32602")
          )

          test (
              "unknown tools and malformed tool calls are Invalid params",
              fun _ ->
                  assertThat
                      (handleRequest server tools """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"missing"}}"""
                       |> errorCode)
                      (isEqualTo "-32602")

                  assertThat
                      (handleRequest
                          server
                          tools
                          """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"echo","arguments":[]}}"""
                       |> errorCode)
                      (isEqualTo "-32602")
          )

          test (
              "tool execution errors and JSON escaping are encoded as MCP results",
              fun _ ->
                  match handleRequest server tools """{"jsonrpc":"2.0","id":"tool","method":"tools/call","params":{"name":"echo"}}""" with
                  | CallTool(id, _) ->
                      let result =
                          completeToolCall
                              id
                              { Content = "bad \"input\"\nnext"
                                IsError = true }
                          |> field "result"

                      assertThat (result |> rawGet "isError" |> rawToJson) (isEqualTo "true")

                      assertThat ((result |> rawGet "content" |> rawToJson).Contains("bad \\\"input\\\"\\nnext")) isTrue
                  | other -> failwith $"Expected a tool call, got %A{other}"
          )

          test (
              "notifications do not produce a JSON-RPC response",
              fun _ ->
                  assertThat
                      (handleRequest server tools """{"jsonrpc":"2.0","method":"notifications/initialized"}""")
                      (isEqualTo NoResponse)
          )

          test (
              "batch requests are rejected",
              fun _ ->
                  let error =
                      handleRequest server tools "[]"
                      |> responseJson
                      |> field "error"

                  assertThat (error |> rawGet "code" |> rawToJson) (isEqualTo "-32600")
          )

          testAsync (
              "streamable HTTP maps notifications to 202",
              fun _ ->
                  toAsync (
                      task {
                          let ctx, readBody = TestContext.create (method = "POST", body = "notification")
                          let! result = streamableHttp (fun _ -> "") next ctx
                          assertThat result.IsSome isTrue
                          assertThat ctx.Response.StatusCode (isEqualTo 202)
                          assertThat (readBody ()) (isEqualTo [||])
                      }
                  )
          )

          testAsync (
              "streamable HTTP maps JSON-RPC responses to 200 application/json",
              fun _ ->
                  toAsync (
                      task {
                          let response = """{"jsonrpc":"2.0","id":1,"result":{}}"""
                          let ctx, readBody = TestContext.create (method = "POST", body = "request")
                          let! result = streamableHttp (fun _ -> response) next ctx
                          assertThat result.IsSome isTrue
                          assertThat ctx.Response.StatusCode (isEqualTo 200)
                          assertThat (ctx.Response.Headers["content-type"]).[0] (isEqualTo "application/json")
                          assertThat (Encoding.UTF8.GetString(readBody ())) (isEqualTo response)
                      }
                  )
          ) ]
    )
