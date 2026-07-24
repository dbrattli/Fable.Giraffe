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

    /// True when a JSON-decoded value is an object rather than a scalar or an array.
    /// `jsx:decode(_, [return_maps])` decodes JSON objects to Erlang maps, so this is
    /// `is_map`. Drives the shared record-reconstruction recursion.
    [<Emit("is_map($0)")>]
    let isJsonObject (value: obj) : bool = nativeOnly

    /// The wire key for a record field. Fable's BEAM backend keys record maps — and thus
    /// `jsx:encode` output and `jsx:decode` input — by the Erlang atom form of the field
    /// name: snake_case, lowercased, with a trailing `_` when the F# name starts lowercase
    /// (Fable's `sanitizeFieldName`). Reflection still reports the pristine F# name via
    /// `PropertyInfo.Name`, so we reproduce that mangling to find the decoded value.
    /// e.g. `FirstName -> first_name`, `lastName -> last_name_`, `HTTPStatus -> h_t_t_p_status`.
    let internal toWireKey (name: string) : string =
        let sb = StringBuilder()

        name
        |> Seq.iteri (fun i c ->
            if Char.IsUpper c then
                if i > 0 then
                    sb.Append('_') |> ignore

                sb.Append(Char.ToLower c) |> ignore
            else
                sb.Append(c) |> ignore)

        if name.Length > 0 && Char.IsLower name.[0] then
            sb.Append('_') |> ignore

        sb.ToString()

    [<Emit("maps:get($1, $0, null)")>]
    let private mapGet (value: obj) (key: string) : obj = nativeOnly

    /// Read a member from a JSON-decoded object (an Erlang map) by its F# field name, or
    /// `null` when absent. The name is mangled to the decoded map's key form first.
    let getJsonMember (value: obj) (name: string) : obj = mapGet value (toWireKey name)
