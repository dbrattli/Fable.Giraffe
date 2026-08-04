namespace Fable.Giraffe

open System
open System.Text
open System.Threading.Tasks

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
    let okAtom: obj = nativeOnly

    /// Read a file's contents as a UTF-8 string. `file:read_file/1` returns
    /// `{ok, Binary}` on success — an Erlang binary, which is already the
    /// representation of an F# string on BEAM, so no decoding is needed.
    /// `path` is absolute or relative to the node's working directory.
    [<Emit("element(2, file:read_file($0))")>]
    let readFileText (path: string) : string = nativeOnly

    /// Start an `Async` and expose it as a `Task`. On BEAM the `task` computation
    /// expression is a CPS alias for `Async` (see Helpers.toAsync), so the two share a
    /// runtime representation and the bridge is the identity — no scheduler hop.
    let startAsTask (computation: Async<'T>) : Task<'T> = unbox computation
