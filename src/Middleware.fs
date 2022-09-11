namespace Fable.Giraffe

open System
open System.Threading.Tasks

open Fable.Giraffe.Pipelines

type App = Func<Scope, unit -> Task<Response>, Request -> Task<unit>, Task<unit>>

module Middleware =
    let useGiraffe (handler: HttpHandler) : App =
        // pre-compile the handler pipeline
        let func: HttpFunc = handler earlyReturn

        let defaultHandler = setStatusCode 404 |> HttpHandler.text ""
        let defaultFunc: HttpFunc = defaultHandler earlyReturn

        let app (scope: Scope) (receive: unit -> Task<Response>) (send: Request -> Task<unit>) = task {
            let ctx = HttpContext(scope, receive, send)
            let! result = func ctx

            match result with
            | None ->
                let! _ = defaultFunc ctx
                ()
            | _ -> ()
        }

        Func<_, _, _, _>(app)
