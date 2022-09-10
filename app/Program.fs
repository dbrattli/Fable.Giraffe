module Program

open Fable.Giraffe
open Fable.Giraffe.Pipelines

type Object = { Name : string; Age : int }

let webApp = choose [
    route "/ping"
    |> HttpHandler.text "pong"
    route "/json"
    |> HttpHandler.json { Name = "Dag"; Age = 53 }
]

let app = Middleware.useGiraffe webApp
