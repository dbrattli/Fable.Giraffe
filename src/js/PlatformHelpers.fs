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

    /// True when a JSON-decoded value is an object rather than a scalar or an array.
    /// Drives the shared record-reconstruction recursion.
    [<Emit("(typeof $0 === 'object' && $0 !== null && !Array.isArray($0))")>]
    let isJsonObject (value: obj) : bool = nativeOnly

    /// Read a member from a JSON-decoded object by name, or `null`/`undefined` when absent.
    [<Emit("$0[$1]")>]
    let getJsonMember (value: obj) (name: string) : obj = nativeOnly
