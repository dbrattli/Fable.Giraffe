namespace Fable.Giraffe.Tests

open Fable.Core
open Fable.Beam.Maps
open Fable.Beam.Cowboy.CowboyReq

open Fable.Giraffe

module Maps = Fable.Beam.Maps

// BEAM/Cowboy-target test-context factory. A fake Cowboy Req *map* with method/path/scheme/headers
// satisfies the cowboy_req:method/path/scheme/headers lookups without a live socket; the response
// is read straight off HttpResponse.Body. The request body is pre-buffered under `giraffe_body`
// (there is no socket for cowboy_req:read_body to stream from) — HttpRequest.GetBodyAsync reads it
// there. A factory (not a subclass) because Fable.Beam inheritance does not carry the base
// HttpContext's fields (e.g. field_response).
module private BeamFakes =

    [<Emit("#{method => $0, path => $1, scheme => <<\"http\">>, headers => $3, giraffe_body => $2}")>]
    let makeReq (method: string) (path: string) (body: string) (headers: BeamMap<string, string>) : Req = nativeOnly


type TestContext =

    static member create
        (?method: string, ?path: string, ?status: int, ?headers: HeaderDictionary, ?body: string, ?services: ServiceCollection)
        : HttpContext * (unit -> byte[]) =
        let _method = defaultArg method "GET"
        let _path = defaultArg path "/"
        let _body = defaultArg body ""

        // Lowercased on the way in, because that is what Cowboy hands a real handler and what
        // HttpRequest.Headers therefore keys on.
        let mutable pairs: (string * string) list = []

        match headers with
        | Some hd ->
            for pair in hd.Scoped do
                pairs <- (pair[0].ToLower(), pair[1]) :: pairs
        | None -> ()

        let ctx = HttpContext(BeamFakes.makeReq _method _path _body (Maps.ofList pairs))
        ctx.SetServices(defaultArg services (ServiceCollection()))
        ctx, (fun () -> ctx.Response.Body)
