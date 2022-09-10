namespace Fable.Giraffe

open System
open System.Collections.Generic
open System.Text.RegularExpressions

type StringValues(strings: string[]) =
    new(str: string) = StringValues [| str |]

    member x.Count = strings.Length
    member x.ToArray() = strings

    member x.Item
        with get (index: int) = strings[index]

    override x.ToString() = String.Join(", ", strings)

    interface IEnumerable<string> with
        member x.GetEnumerator() =
            (strings :> IEnumerable<string>).GetEnumerator()

        member x.GetEnumerator() : System.Collections.IEnumerator =
            (strings :> System.Collections.IEnumerable).GetEnumerator()

    static member Empty = StringValues [||]



[<AutoOpen>]
module Helpers =
    /// <summary>
    /// Checks if an object is not null.
    /// </summary>
    /// <param name="x">The object to validate against `null`.</param>
    /// <returns>Returns true if the object is not null otherwise false.</returns>
    let inline isNotNull x = not (isNull x)

    /// <summary>
    /// Converts a string into a string option where null or an empty string will be converted to None and everything else to Some string.
    /// </summary>
    /// <param name="str">The string value to be converted into an option of string.</param>
    /// <returns>Returns None if the string was null or empty otherwise Some string.</returns>
    let inline strOption (str: string) =
        if String.IsNullOrEmpty str then None else Some str

    let dashify (separator: string) (input: string) =
        Regex.Replace(
            input,
            "[a-z]?[A-Z]",
            fun m ->
                if m.Value.Length = 1 then
                    m.Value.ToLowerInvariant()
                else
                    m.Value.Substring(0, 1)
                    + separator
                    + m.Value.Substring(1, 1).ToLowerInvariant()
        )

    let removeNamespace (fullName: string) =
        fullName.Split('.')
        |> Array.last
        |> (fun name -> name.Replace("`", "_"))


// /// <summary>
// /// Reads a file asynchronously from the file system.
// /// </summary>
// /// <param name="filePath">The absolute path of the file.</param>
// /// <returns>Returns the string contents of the file wrapped in a Task.</returns>
// let readFileAsStringAsync (filePath : string) =
//     task {
//         use reader = new StreamReader(filePath)
//         return! reader.ReadToEndAsync()
//     }
