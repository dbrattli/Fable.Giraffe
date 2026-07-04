module Program

// Test entry for the JS backend: defines the same handlers as the example app
// but exports `webApp` instead of starting a server, so the Node smoke test can
// drive it in-process via Server.toMiddleware.

open Fable.Giraffe
open Fable.Giraffe.Pipelines

type Model = { Name: string; Age: int }

let echo: HttpHandler =
    fun next (ctx: HttpContext) ->
        task {
            let! body = ctx.ReadBodyFromRequestAsync()
            return! text body next ctx
        }

let webApp: HttpHandler =
    choose
        [ route "/ping" |> HttpHandler.text "pong"

          route "/json"
          |> HttpHandler.json { Name = "Dag"; Age = 53 }

          route "/echo" >=> echo ]

// Build the Connect middleware inside F# (as the real API does) and export it,
// so the Node smoke test never applies the handler from hand-written JS — that
// round-trip is what de-normalizes Fable's function representation.
let middleware = Server.toMiddleware webApp (ServiceCollection())
