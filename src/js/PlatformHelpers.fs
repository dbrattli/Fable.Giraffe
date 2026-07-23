namespace Fable.Giraffe

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
