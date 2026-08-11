module McpExample

open Fable.Giraffe
open Fable.Giraffe.Mcp

/// PascalCase fields produce the same camelCase MCP wire names on every Fable target.
type GreetInput = { Name: string }

let server =
    { Info =
        { Name = "fable-giraffe-example"
          Version = "1.0.0" }
      ProtocolVersions = [ "2025-11-25" ] }

let tools =
    [ Tools.defineSync "greet" (fun input ->
          { Content = $"Hello, %s{input.Name}!"
            IsError = false })
      |> Tools.description "Greet someone by name" ]

/// The same typed tool and Streamable HTTP handler run on Python, JavaScript and BEAM.
let handler: HttpHandler = Tools.host server tools
