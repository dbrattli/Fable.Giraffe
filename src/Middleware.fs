namespace Fable.Giraffe

open System
open System.Threading.Tasks

open Fable.Giraffe.Pipelines

type App = Func<Scope, unit -> Task<Response>, Request -> Task<unit>, Task<unit>>

module Middleware =
    let useGiraffe (handler: HttpHandler) : App =
        // pre-compile the handler pipeline
        let func: HttpFunc = handler earlyReturn

        let defaultHandler = setStatusCode 404 |> HttpHandler.text "Not found!"
        let defaultFunc: HttpFunc = defaultHandler earlyReturn

        let app (scope: Scope) (receive: unit -> Task<Response>) (send: Request -> Task<unit>) =
            task {
                if scope["type"] = "http" then
                    let ctx = HttpContext(scope, receive, send)
                    let! result = func ctx
                    if result.IsNone then
                        let! _ = defaultFunc ctx
                        return ()
                    else
                        return ()
                else
                    return ()
            }

        // Return a tupled function so it may be used from Python
        Func<_, _, _, _>(app)
