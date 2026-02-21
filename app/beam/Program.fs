module Program

open Fable.Giraffe
open Fable.Giraffe.Pipelines

type Model = { Name: string; Age: int }

let webApp =
    choose [
        route "/ping" |> HttpHandler.text "pong"

        route "/json"
        |> HttpHandler.json { Name = "Dag"; Age = 53 }
    ]

let start () =
    WebHostBuilder()
        .Configure(fun app ->
            app.UseGiraffe(webApp))
        .Build(8080)
