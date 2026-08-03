module Program

open Fable.Giraffe
// Opened after `Fable.Giraffe`, so `route`, `routef` and the verb functions here shadow their
// HttpHandler-returning namesakes. Classic `choose [ ... ]` composition still works — this
// layer is opt-in, and only apps that want an API description need it.
open Fable.Giraffe.Endpoints

open Fable.Logging

type Model = { Name: string; Age: int }

let loggingHandler: HttpHandler =
    fun next (ctx: HttpContext) ->
        task {
            let log = ctx.GetService<ILogger>()
            log.LogDebug("Hello from Fable.Giraffe!")

            return! text "Logged" next ctx
        }

let endpoints =
    [ GET
          [ route "/" (htmlFile "public/index.html")

            route "/ping" (text "pong")
            |> summary "Health check"
            |> description "Answers `pong` if the server is up."
            |> tags [ "meta" ]
            // Not `responds<string>`, which would document application/json — this handler
            // writes text/plain, and the document should say what the handler does.
            |> respondsWith<string> 200 "text/plain"

            route "/json" (json { Name = "Dag"; Age = 53 })
            |> summary "A sample model"
            |> responds<Model> 200

            route "/log" loggingHandler |> tags [ "meta" ] ]

      POST
          [ // A body that does not fit `Model` is answered with 422 and a per-field error
            // list, rather than throwing — see Core.validateJson.
            route "/greet" (validateJson<Model> (fun m -> text $"Hello, {m.Name}"))
            |> summary "Greet someone"
            |> accepts<Model>
            |> respondsWith<string> 200 "text/plain"
            |> respondsWith<string> 422 "application/json" ] ]

let webApp =
    endpoints
    |> OpenApi.withDocs (OpenApiInfo.Create("Fable.Giraffe Example", "1.0"))
    |> Endpoints.toHandler

let app =
    WebHostBuilder()
        .ConfigureLogging(fun builder -> builder.SetMinimumLevel(LogLevel.Debug))
        .UseStructlog()
        .Configure(fun app ->
            app.UseStaticFiles("/static", "public")
            app.UseGiraffe(webApp))
        .Build()
