namespace Fable.Giraffe

open Fable.Core

[<AutoOpen>]
module PlatformHelpers =
    /// Get the length of a byte array (atomics-backed tuple on BEAM).
    [<Emit("fable_utils:byte_array_length($0)")>]
    let len (x: 'T) : int = Seq.length x

    /// On BEAM, integers are already native — this is an identity function.
    let inline toNativeInt (x: int) : int = x

    /// Convert a Fable byte array ({byte_array, Size, Ref}) to an Erlang binary for Cowboy.
    [<Emit("list_to_binary(fable_utils:byte_array_to_list($0))")>]
    let byteArrayToBinary (bytes: byte[]) : byte[] = bytes

    [<Emit("ok")>]
    let okAtom : obj = nativeOnly
