namespace Fable.Giraffe.Tests

open Fable.Core
open Fable.Beam.Cowboy.CowboyReq

open Fable.Giraffe

// BEAM/Cowboy-target test-context factory. A fake Cowboy Req *map* with method/path/scheme
// satisfies the cowboy_req:method/path/scheme lookups without a live socket; the response is read
// straight off HttpResponse.Body. The request body is pre-buffered under `giraffe_body` (there is
// no socket for cowboy_req:read_body to stream from) — HttpRequest.GetBodyAsync reads it there.
// A factory (not a subclass) because Fable.Beam inheritance does not carry the base HttpContext's
// fields (e.g. field_response).
module private BeamFakes =

    [<Emit("#{method => $0, path => $1, scheme => <<\"http\">>, giraffe_body => $2}")>]
    let makeReq (method: string) (path: string) (body: string) : Req = nativeOnly


type TestContext =

    static member create
        (?method: string, ?path: string, ?status: int, ?headers: HeaderDictionary, ?body: string, ?services: ServiceCollection)
        : HttpContext * (unit -> byte[]) =
        let _method = defaultArg method "GET"
        let _path = defaultArg path "/"
        let _body = defaultArg body ""
        let ctx = HttpContext(BeamFakes.makeReq _method _path _body)
        ctx.SetServices(defaultArg services (ServiceCollection()))
        ctx, (fun () -> ctx.Response.Body)
