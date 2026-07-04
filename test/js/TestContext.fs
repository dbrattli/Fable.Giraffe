namespace Fable.Giraffe.Tests

open Fable.Core
open Fable.Core.JsInterop

open Fable.Giraffe

// JS/Node-target test-context factory. Builds fake `http` IncomingMessage / ServerResponse
// (request stream replays the body; response captures the bytes written by end()) and returns
// a real HttpContext plus a reader thunk. A factory rather than a subclass so every backend
// shares the same `TestContext.create` seam (Fable.Beam can't inherit base fields).
module private JsFakes =

    [<Emit("(() => { const chunks = $1 ? [Buffer.from($1)] : []; return { method: $0, url: $2, headers: $3, on(ev, cb){ if (ev==='data') { for (const c of chunks) cb(c); } else if (ev==='end') { cb(); } } }; })()")>]
    let makeReq (method: string) (body: string) (url: string) (headers: obj) : IncomingMessage = nativeOnly

    [<Emit("({ statusCode: 200, _body: new Uint8Array(0), setHeader(k, v){ (this._headers = this._headers || {})[k.toLowerCase()] = v; }, end(c){ this._body = c == null ? new Uint8Array(0) : c; } })")>]
    let makeRes () : ServerResponse = nativeOnly

    [<Emit("$0._body")>]
    let capturedBody (res: ServerResponse) : byte[] = nativeOnly


type TestContext =

    static member create
        (?method: string, ?path: string, ?status: int, ?headers: HeaderDictionary, ?body: string)
        : HttpContext * (unit -> byte[]) =
        let _method = defaultArg method "GET"
        let _path = defaultArg path "/"
        let _body = defaultArg body ""

        // Convert the HeaderDictionary into a plain JS { name: value } object.
        let headersObj = createObj []

        match headers with
        | Some hd ->
            for pair in hd.Scoped do
                headersObj?(pair[0].ToLower()) <- pair[1]
        | None -> ()

        let req = JsFakes.makeReq _method _body _path headersObj
        let res = JsFakes.makeRes ()
        let ctx = HttpContext(req, res, ServiceCollection())
        ctx, (fun () -> JsFakes.capturedBody res)
