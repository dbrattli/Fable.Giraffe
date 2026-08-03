module Program

open Fable.Giraffe
// Opened after `Fable.Giraffe`, so `route` and the verb functions here shadow their
// HttpHandler-returning namesakes. Classic `choose [ ... ]` composition still works.
open Fable.Giraffe.Endpoints

open Fable.Logging

type Model = { Name: string; Age: int }

let endpoints =
    [ GET
          [ route "/ping" (text "pong")
            |> summary "Health check"
            |> respondsWith<string> 200 "text/plain"

            route "/json" (json { Name = "Dag"; Age = 53 })
            |> summary "A sample model"
            |> responds<Model> 200 ]

      POST
          [ route "/greet" (validateJson<Model> (fun m -> text $"Hello, {m.Name}"))
            |> summary "Greet someone"
            |> accepts<Model>
            |> respondsWith<string> 200 "text/plain" ] ]

/// The OpenAPI document is rendered once here, at composition time, and the spec handler
/// closes over the resulting string. That is what makes it work under Cowboy, which spawns a
/// fresh process per request: an immutable string travels, where a lazily-populated cache
/// (a class with a mutable field, hence a process-dictionary ref) would read back `undefined`.
let webApp =
    endpoints
    |> OpenApi.withDocs (OpenApiInfo.Create("Fable.Giraffe BEAM Example", "1.0"))
    |> Endpoints.toHandler

let start () =
    WebHostBuilder()
        .ConfigureLogging(fun builder -> builder.SetMinimumLevel(LogLevel.Debug))
        .UseBeamLogging()
        .Configure(fun app ->
            app.UseStaticFiles("/static", "app/public")
            app.UseGiraffe(webApp))
        .Build(8080)
