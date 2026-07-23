namespace Fable.Giraffe

open Fable.Core

[<AutoOpen>]
module PlatformHelpers =
    [<Fable.Core.Emit("len($0)")>]
    let len (x: 'T) : int = Seq.length x

    /// Convert Fable's int32 wrapper to a plain Python int.
    /// ASGI (h11/uvicorn) requires native int for status codes etc.
    [<Fable.Core.Emit("int($0)")>]
    let toNativeInt (x: int) : int = x

    /// Read a file's contents as a UTF-8 string. Synchronous (blocks the event
    /// loop); `path` is absolute or relative to the process working directory.
    /// CPython closes the handle via refcounting once `read()` returns.
    [<Emit("open($0, encoding='utf-8').read()")>]
    let readFileText (path: string) : string = nativeOnly
