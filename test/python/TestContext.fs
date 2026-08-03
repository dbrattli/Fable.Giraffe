namespace Fable.Giraffe.Tests

open System.Collections.Generic
open System.Threading.Tasks
open System.Text

open Fable.Giraffe

// Python-target test-context factory. Builds a real ASGI-shaped HttpContext with fake
// scope/receive/send that capture the response body, and returns it alongside a reader thunk.
// A factory (not an HttpContext subclass) because Fable.Beam can't carry base fields through
// inheritance — so every backend exposes the same `TestContext.create` seam the shared tests use.
type TestContext =

    static member create
        (?method: string, ?path: string, ?status: int, ?headers: HeaderDictionary, ?body: string, ?services: ServiceCollection)
        : HttpContext * (unit -> byte[]) =
        let _method = defaultArg method "GET"
        let _path = defaultArg path "/"
        let services = defaultArg services (ServiceCollection())

        let _headers =
            defaultArg headers (HeaderDictionary())
            |> (fun x -> x.Scoped)

        let _scope =
            Dictionary<string, obj>(
                dict
                    [ "method", _method :> obj
                      "path", _path
                      "headers", _headers
                      "services", services ]
            )

        let _body = ResizeArray<byte array>()

        let send (response: Response) =
            task {
                for KeyValue(key, value) in response do
                    if key = "body" then
                        _body.Add(value :?> byte array)
            }

        let receive () =
            task {
                let bytes =
                    body
                    |> Option.defaultValue ""
                    |> Encoding.UTF8.GetBytes

                return Dictionary<string, obj>(dict [ "body", bytes :> obj ])
            }

        let ctx = HttpContext(_scope, receive, send)
        ctx, (fun () -> _body[0])
