module Fable.Giraffe.Json

open Fable.Core

/// JSON encoding/decoding using jsx (pure Erlang JSON library).

[<Emit("jsx:encode($0)")>]
let private jsonEncode (term: obj) : string = nativeOnly

[<Emit("jsx:decode($0, [return_maps])")>]
let jsonDecode (binary: byte array) : obj = nativeOnly

/// Serialize an F# value to a JSON string (binary on BEAM).
let serialize (value: obj) : string =
    jsonEncode value

/// Deserialize a JSON string to an Erlang term (map, list, etc).
let inline deserialize (s: string) : obj =
    jsonDecode (unbox s)
