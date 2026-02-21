namespace Fable.Giraffe

[<AutoOpen>]
module PlatformHelpers =
    [<Fable.Core.Emit("len($0)")>]
    let len (x: 'T) : int = Seq.length x

    /// Convert Fable's int32 wrapper to a plain Python int.
    /// ASGI (h11/uvicorn) requires native int for status codes etc.
    [<Fable.Core.Emit("int($0)")>]
    let toNativeInt (x: int) : int = x
