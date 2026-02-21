/// FFI bindings for Cowboy's cowboy_req module.
/// See: https://ninenines.eu/docs/en/cowboy/2.12/manual/cowboy_req/
module Fable.Giraffe.CowboyReq

open Fable.Core

/// Opaque Cowboy request object. Passed through all cowboy_req:* calls.
type Req = obj

/// Get the HTTP method (e.g. <<"GET">>, <<"POST">>).
[<Emit("cowboy_req:method($0)")>]
let method' (req: Req) : string = nativeOnly

/// Get the request path (e.g. <<"/api/users">>).
[<Emit("cowboy_req:path($0)")>]
let path (req: Req) : string = nativeOnly

/// Get a specific header value. Returns undefined/error if not found.
[<Emit("cowboy_req:header($0, $1)")>]
let header (name: string) (req: Req) : string = nativeOnly

/// Get a specific header value with a default.
[<Emit("cowboy_req:header($0, $1, $2)")>]
let headerDefault (name: string) (req: Req) (defaultValue: string) : string = nativeOnly

/// Get all request headers as a map.
[<Emit("cowboy_req:headers($0)")>]
let headers (req: Req) : obj = nativeOnly

/// Read the full request body. Returns {ok, Body, Req}.
/// The Body is an Erlang binary.
[<Emit("cowboy_req:read_body($0)")>]
let readBody (req: Req) : obj * byte array * Req = nativeOnly

/// Get the query string.
[<Emit("cowboy_req:qs($0)")>]
let queryString (req: Req) : string = nativeOnly

/// Send a full response: status, headers map, body, req.
/// Returns the updated Req.
[<Emit("cowboy_req:reply($0, $1, $2, $3)")>]
let reply (status: int) (headers: obj) (body: byte array) (req: Req) : Req = nativeOnly

/// Send a response with status and headers only (no body).
[<Emit("cowboy_req:reply($0, $1, $2)")>]
let replyNoBody (status: int) (headers: obj) (req: Req) : Req = nativeOnly

/// Get the peer IP address and port.
[<Emit("cowboy_req:peer($0)")>]
let peer (req: Req) : obj * int = nativeOnly

/// Get the scheme (<<"http">> or <<"https">>).
[<Emit("cowboy_req:scheme($0)")>]
let scheme (req: Req) : string = nativeOnly

/// Get the host.
[<Emit("cowboy_req:host($0)")>]
let host (req: Req) : string = nativeOnly

/// Get the port number.
[<Emit("cowboy_req:port($0)")>]
let port (req: Req) : int = nativeOnly
