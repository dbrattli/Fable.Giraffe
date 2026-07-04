namespace Fable.Giraffe

open System.Threading.Tasks

// Derived HttpContext members shared by every backend, factored the same way the
// original .NET Giraffe factors HttpContextExtensions.fs: these are built purely on
// the per-target *primitives* (Request.Method, Response.WriteAsync/SetHttpHeader/…,
// RequestServices) that each backend's HttpContext.fs defines, so they are identical
// across Python/BEAM/JS and live here once. Compiled per-target (linked after each
// HttpContext.fs), so `ctx` resolves to whichever concrete class the target links in.
//
// NOTE: ReadBodyFromRequestAsync / BindJsonAsync are deliberately NOT here — they sit
// on the request-body encoding seam where BEAM genuinely differs (Cowboy hands back a
// binary/string, Python/JS a byte[]), so they stay in each target's HttpContext.fs.

[<AutoOpen>]
module HttpContextExtensions =

    type HttpContext with

        member ctx.SetStatusCode(statusCode: int) = ctx.Response.SetStatusCode(statusCode)

        member ctx.SetHttpHeader(key: string, value: obj) = ctx.Response.SetHttpHeader(key, value)

        member ctx.SetContentType(contentType: string) =
            ctx.SetHttpHeader(HeaderNames.ContentType, contentType)

        member ctx.WriteBytesAsync(bytes: byte[]) =
            task {
                ctx.SetHttpHeader(HeaderNames.ContentLength, len bytes)

                // HEAD: send status + headers (including the computed Content-Length) with
                // an empty body. Skipping WriteAsync entirely — as the old Python/BEAM code
                // did — would never *start* the response on those backends, since WriteAsync
                // is what flushes status+headers here (unlike ASP.NET Core).
                if HttpMethods.IsHead ctx.Request.Method then
                    do! ctx.Response.WriteAsync([||])
                else
                    do! ctx.Response.WriteAsync(bytes)

                return Some ctx
            }

        member inline x.GetService<'T>() : 'T =
            let (Singleton service) = x.RequestServices.GetService(typeof<'T>)
            service :?> 'T
