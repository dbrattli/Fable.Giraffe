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

        /// Serve files from <c>directory</c> under the URL prefix <c>requestPath</c> (e.g.
        /// <c>UseStaticFiles("/static", "public")</c> maps <c>GET /static/app.css</c> to
        /// <c>public/app.css</c>). An empty <c>requestPath</c> mounts at the root. Requests that
        /// don't resolve to a file fall through to the rest of the pipeline (i.e. Giraffe). This
        /// matches the BEAM/JS backends' 2-arg form; backed here by Starlette's StaticFiles.
        member x.UseStaticFiles(requestPath: string, directory: string) : unit =
            x.UseMiddleware(fun app loggerFactory ->
                let middleware = StaticFiles.Create(directory)

                let asgi (scope: Scope) (receive: unit -> Task<Response>) (send: Request -> Task<unit>) =
                    task {
                        let path = scope["path"] :?> string

                        let matches =
                            requestPath = ""
                            || path = requestPath
                            || path.StartsWith(requestPath + "/")

                        if matches then
                            // Strip the mount prefix so Starlette's StaticFiles resolves the file
                            // relative to `directory` (the same rewrite a Starlette Mount does).
                            // On a miss StaticFiles raises, so restore the original path before the
                            // fall-through to Giraffe.
                            let stripped = path.Substring(requestPath.Length)
                            scope["path"] <- (if stripped = "" then "/" else stripped)

                            try
                                do! middleware.InvokeAsync(scope, receive, send)
                            with _ ->
                                scope["path"] <- path
                                do! app.Invoke(scope, receive, send)
                        else
                            do! app.Invoke(scope, receive, send)
                    }

                Func<Scope, unit -> Task<Response>, Request -> Task<unit>, Task<unit>>(asgi))
            |> ignore

        /// Serve files from <c>directory</c> at the URL root, falling through to Giraffe on a
        /// miss. Shorthand for <c>UseStaticFiles("", directory)</c>.
        member x.UseStaticFiles(directory: string) : unit = x.UseStaticFiles("", directory)
