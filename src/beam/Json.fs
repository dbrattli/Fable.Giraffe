(**
# Json — the BEAM binding to Fable.TypedJson

Every backend used to carry a hand-written serializer: Python walked `__slots__`
and stripped Fable's trailing underscores, JS called `JSON.stringify` raw, BEAM
called `jsx:encode`. They disagreed about field names — the same record reached
the wire as `Description` on JS and `description` on Python and BEAM — and none
of them could describe itself, so an OpenAPI document had nothing truthful to
generate from.

TypedJson produces a type's decoder, encoder and JSON Schema from one walk, with
an invariant that the three cannot drift. The spec therefore cannot describe a
field the serializer does not emit.

## Build codecs at composition time, never per request

A codec costs ~193µs (flat) to ~1ms (nested) to construct and TypedJson has no
memo cache, so constructing one per request would be a serious regression on a
path that used to be a single `json.dumps`. Giraffe already pre-evaluates
handlers at startup — `Core.text` computes its bytes outside the per-request
lambda — and the JSON handlers do the same with their codecs.

invariant: a codec is built where a handler is composed, not where it runs
*)

module Fable.Giraffe.Json

open Fable.TypedJson.Schema
open Fable.TypedJson.Json
open Fable.TypedJson.Beam.Json

/// The codec for `'T`. `inline` so `typeof<'T>` is resolved at the call site —
/// Fable erases generics, so a non-inline version would capture nothing.
let inline codec<'T> () : TypedJson<'T> = auto<'T> ()

/// The codec for a type known only at run time. Remoting recovers its methods'
/// argument and return types by reflection, so it has a `System.Type` and no
/// `'T` to speak of; `buildCodec` takes the type directly.
let codecFor (t: System.Type) : TypedJson<obj> =
    Fable.TypedJson.Json.buildCodec<obj> beam emptyRegistry t

/// Serialize a value of statically known type. Prefer hoisting `codec<'T> ()`
/// out of a hot path over calling this repeatedly.
let inline serialize<'T> (value: 'T) : string = (codec<'T> ()).encode value

/// Serialize a value whose type is only known reflectively.
let serializeAs (t: System.Type) (value: obj) : string = (codecFor t).encode value

/// Parse into the backend's native JSON representation — a Python `dict`, a JS
/// object, an Erlang map. This is the raw form; `tryDeserialize` decodes into
/// an F# type.
let deserialize (s: string) : obj = parseRaw s

/// Decode into `'T`, accumulating a per-field error list rather than throwing.
let inline tryDeserialize<'T> (s: string) : Result<'T, FieldError list> = (codec<'T> ()).decode (parseRaw s)
