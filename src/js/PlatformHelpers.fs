namespace Fable.Giraffe

open System
open System.Threading.Tasks

open Fable.Core

[<AutoOpen>]
module PlatformHelpers =
    /// Length of a sequence/byte array. On JS this is a plain number; no
    /// boxed-int wrapper to unwrap (unlike the Python/BEAM backends).
    let len (x: 'T) : int = Seq.length x

    /// On JS there is no separate boxed-int type, so this is the identity.
    let inline toNativeInt (x: int) : int = x

    /// `fs.readFileSync`. The encoding arg is required — without it Node returns
    /// a Buffer rather than a string.
    [<Import("readFileSync", "node:fs")>]
    let private readFileSync (path: string) (encoding: string) : string = nativeOnly

    /// Read a file's contents as a UTF-8 string. Synchronous (blocks the event
    /// loop); `path` is absolute or relative to the process working directory.
    let readFileText (path: string) : string = readFileSync path "utf8"

    /// Start an `Async` and expose it as a `Task`. fable-library-js has no `startAsTask`
    /// (`Async.StartAsTask` fails to link), but a `Task<'T>` is a `Promise<'T>` at runtime
    /// on this backend, so starting as a promise is the equivalent bridge.
    let startAsTask (computation: Async<'T>) : Task<'T> =
        Async.StartAsPromise computation
        |> unbox<Task<'T>>

    /// Turn a JSON-decoded argument into the record instance the handler expects.
    /// On JS this is the identity: Fable compiles records to plain objects keyed by
    /// the field names, so `JSON.parse` already yields a structurally valid record
    /// (verified: copy-update and re-serialization both round-trip). Python needs a
    /// real conversion because its records are `__slots__` classes.
    let convertJsonArg (value: obj) (_targetType: Type) : obj = value
