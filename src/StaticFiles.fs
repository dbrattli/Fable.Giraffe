namespace Fable.Giraffe

open System
open System.Threading.Tasks

open Fable.Core

[<Import("StaticFiles", "starlette.staticfiles")>]
type StaticFiles =
    [<Emit("$0($1, $2, $3)")>]
    abstract member InvokeAsync: Scope * (unit -> Task<Response>) * (Request -> Task<unit>) -> Task<unit>

[<Erase>]
type StaticFilesStatic =
    [<Emit("$0(directory=$1)")>]
    abstract member Create: string -> StaticFiles

[<AutoOpen>]
module StaticFilesMiddleware =
    [<Import("StaticFiles", "starlette.staticfiles")>]
    let StaticFiles: StaticFilesStatic = nativeOnly

    type IApplicationBuilder with

        member x.UseStaticFiles(directory: string) : unit =
            x.UseMiddleware(fun app loggerFactory ->
                let middleware = StaticFiles.Create(directory)

                let inline asgi (scope: Scope) (receive: unit -> Task<Response>) (send: Request -> Task<unit>) = task {
                    try
                        do! middleware.InvokeAsync(scope, receive, send)
                    with ex ->
                        do! app.Invoke(scope, receive, send)
                }

                Func<Scope, unit -> Task<Response>, Request -> Task<unit>, Task<unit>>(asgi))
            |> ignore
