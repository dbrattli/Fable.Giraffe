module Fable.Giraffe.Json

open Fable.Core

/// Erlang JSON encoding using the built-in json module (OTP 27+)
/// or thoas/jsx for earlier versions. We use Emit to call the
/// Erlang json module directly.

/// Use giraffe_json_encoder to avoid module name collision with OTP's json module.
/// (Fable.Giraffe.Json compiles to json.erl which shadows OTP's json module.)
[<Emit("giraffe_json_encoder:encode($0)")>]
let private jsonEncode (term: obj) : string = nativeOnly

[<Emit("giraffe_json_encoder:decode($0)")>]
let jsonDecode (binary: byte array) : obj = nativeOnly

[<Emit("maps:from_list($0)")>]
let private mapsFromList (proplist: obj) : obj = nativeOnly

[<Emit("erlang:is_map($0)")>]
let private isMap (o: obj) : bool = nativeOnly

[<Emit("maps:to_list($0)")>]
let private mapsToList (o: obj) : (string * obj) array = nativeOnly

[<Emit("erlang:atom_to_binary($0)")>]
let private atomToBinary (o: obj) : string = nativeOnly

[<Emit("erlang:is_atom($0)")>]
let private isAtom (o: obj) : bool = nativeOnly

/// Serialize an F# value to a JSON string (binary on BEAM).
/// Relies on Fable-BEAM's record→map compilation (Phase 3).
/// giraffe_json_encoder:encode/1 returns a binary directly.
let serialize (value: obj) : string =
    jsonEncode value

/// Deserialize a JSON string to an Erlang term (map, list, etc).
let inline deserialize (s: string) : obj =
    jsonDecode (unbox s)
