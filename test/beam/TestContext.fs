namespace Fable.Giraffe.Tests

open Fable.Core
open Fable.Beam.Cowboy.CowboyReq

open Fable.Giraffe

// BEAM/Cowboy-target test-context factory. The shared Handler/Routing suite never reads the
// request body (only Remoting does, and that's Python-only), so a fake Cowboy Req *map* with
// method/path/scheme satisfies the cowboy_req:method/path/scheme lookups without a live socket;
// the response is read straight off HttpResponse.Body. A factory (not a subclass) because
// Fable.Beam inheritance does not carry the base HttpContext's fields (e.g. field_response).
module private BeamFakes =

    [<Emit("#{method => $0, path => $1, scheme => <<\"http\">>}")>]
    let makeReq (method: string) (path: string) : Req = nativeOnly


type TestContext =

    static member create
        (?method: string, ?path: string, ?status: int, ?headers: HeaderDictionary, ?body: string)
        : HttpContext * (unit -> byte[]) =
        let _method = defaultArg method "GET"
        let _path = defaultArg path "/"
        let ctx = HttpContext(BeamFakes.makeReq _method _path)
        ctx, (fun () -> ctx.Response.Body)
