module Fable.Giraffe.Json

open Fable.Core

// JSON serialization for the JS/Node backend.
//
// Fable compiles F# records to plain JS objects whose own-enumerable keys are
// the record field names, so `JSON.stringify`/`JSON.parse` round-trip those
// correctly without a custom replacer. This is intentionally simpler than the
// Python backend's `giraffeDefault` (which reflects over `__slots__` and strips
// keyword-collision underscores). Union/DU serialization is not yet at parity
// across backends — see the JS-target tech-debt notes.

/// Serialize a value to a JSON string using the JS runtime.
let serialize (value: obj) : string = JS.JSON.stringify (value)

/// Deserialize a JSON string into a JS object (record-shaped for `unbox<'T>`).
let deserialize (s: string) : obj = JS.JSON.parse (s)
