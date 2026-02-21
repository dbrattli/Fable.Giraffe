module Fable.Giraffe.Json

open Fable.Core
open Fable.Python.Json

[<Emit("{slot.rstrip('_'): getattr($0, slot) for slot in $0.__slots__}")>]
let private slotsToDict (o: obj) : obj = nativeOnly

[<Emit("type($0).__name__")>]
let private typeName (o: obj) : string = nativeOnly

[<Emit("hasattr($0, $1)")>]
let private hasattr (o: obj) (name: string) : bool = nativeOnly

[<Emit("getattr($0, $1)")>]
let private getattr' (o: obj) (name: string) : obj = nativeOnly

[<Emit("int($0)")>]
let private toInt (o: obj) : obj = nativeOnly

[<Emit("float($0)")>]
let private toFloat (o: obj) : obj = nativeOnly

[<Emit("list($0)")>]
let private toList (o: obj) : obj = nativeOnly

[<Emit("getattr(type($0), 'cases', lambda: [])()")>]
let private getCases (o: obj) : string array = nativeOnly

[<Emit("([$1] + list($0.fields)) if $0.fields else $1")>]
let private unionToList (o: obj) (caseName: string) : obj = nativeOnly

[<Emit("(_ for _ in ()).throw(TypeError(f'Object of type {type($0).__name__} is not JSON serializable'))")>]
let private raiseTypeError (o: obj) : obj = nativeOnly

/// Custom default handler that strips trailing underscores from record slot names.
/// This works around Fable 5's convention of adding trailing underscores to Python identifiers.
let giraffeDefault (o: obj) : obj =
    let name = typeName o

    match name with
    | "Int8"
    | "Int16"
    | "Int32"
    | "Int64"
    | "UInt8"
    | "UInt16"
    | "UInt32"
    | "UInt64" -> toInt o
    | "Float32"
    | "Float64" -> toFloat o
    | "FSharpArray"
    | "GenericArray"
    | "Int8Array"
    | "Int16Array"
    | "Int32Array"
    | "Int64Array"
    | "UInt8Array"
    | "UInt16Array"
    | "UInt32Array"
    | "UInt64Array"
    | "Float32Array"
    | "Float64Array"
    | "FSharpList" -> toList o
    | _ ->
        if hasattr o "tag" && hasattr o "fields" then
            let cases = getCases o
            let tag: int = getattr' o "tag" :?> int

            let caseName =
                if tag < cases.Length then cases.[tag]
                else "Case" + string tag

            unionToList o caseName
        elif hasattr o "__slots__" then
            slotsToDict o
        else
            raiseTypeError o

/// Serialize an object to a JSON string, stripping trailing underscores from record field names.
let serialize (value: obj) : string =
    json.dumps (value, ``default`` = giraffeDefault)

/// Deserialize a JSON string to a Python object (dict, list, etc).
let inline deserialize (s: string) : obj =
    Json.loads s
