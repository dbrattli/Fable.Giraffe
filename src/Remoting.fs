module Fable.Giraffe.Remoting

open System
open System.Text
open System.Text.RegularExpressions

open FSharp.Reflection
open Fable.SimpleJson.Python

let createApi() =
    ()

let private dashify (separator: string) (input: string) =
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

let dashifyRoute (path: string) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let segment = SubRouting.getNextPartOfPath ctx |> dashify "_"
        if segment.Equals path then
            next ctx
        else
            skipPipeline ()

let removeNamespace (fullName: string) =
    fullName.Split('.')
    |> Array.last
    |> (fun name -> name.Replace("`", "_"))

type Arity1<'A, 'B> = 'A -> Async<'B>
type Arity2<'A, 'B, 'C> = 'A -> 'B -> Async<'C>
type Arity3<'A, 'B, 'C, 'D> = 'A -> 'B -> 'C-> Async<'D>
type Arity4<'A, 'B, 'C, 'D, 'E> = 'A -> 'B -> 'C -> 'D -> Async<'E>

[<RequireQualifiedAccess>]
type Signature<'A, 'B, 'C, 'TResult> =
    | Arity1 of Arity1<'A, 'TResult>
    | Arity2 of Arity2<'A, 'B, 'TResult>
    | Arity3 of Arity3<'A, 'B, 'C, 'TResult>

    member x.Invoke(args: List<obj>) =
        match x with
        | Arity1 f -> f (args[0] :?> _)
        | Arity2 f -> f (args[0] :?> _) (args[1] :?> _)
        | Arity3 f -> f (args[0] :?> _) (args[1] :?> _) (args[2] :?> _)

    static member Create (value: obj, arity: int) =
        match arity with
        | 2 -> value :?> Arity1<_,_> |> Signature.Arity1
        | 3 -> value :?> Arity2<_,_,_> |> Signature.Arity2
        | 4 -> value :?> Arity3<_,_,_,_> |> Signature.Arity3
        | _ -> failwith "Only methods with 1, 2 or 3 arguments are supported"

let readArguments (ctx: HttpContext) (argumentTypes: Type array) =
    task {
        let! json = ctx.ReadBodyFromRequestAsync()

        let inputJson = SimpleJson.parseNative json
        match inputJson with
        | Json.JArray args ->
            let args =
                args
                |> List.mapi (fun i arg ->
                    let typeInfo = createTypeInfo argumentTypes[i]
                    Convert.fromJson<_> arg typeInfo)
            return args
        | _ -> return failwith "Expected an array of arguments"
    }

let inline fromValue (api: 'T)  () =
    let typ = api.GetType()
    let apiName = removeNamespace typ.FullName

    let fields = FSharpType.GetRecordFields typ

    subRoute $"/{apiName}" (choose [
        for field in fields do
            let value = field.GetValue api

            let propType = field.PropertyType
            let argCount = propType.GetGenericArguments().Length
            let fsharpFuncArgs = propType.GetGenericArguments()
            let argumentTypes = fsharpFuncArgs.[0 .. fsharpFuncArgs.Length - 2]
            let asyncOfResult = fsharpFuncArgs.[fsharpFuncArgs.Length - 1]
            let resultType = asyncOfResult.GetGenericArguments()[0]

            let method = Signature.Create<_,_,_,_>(value, argCount)

            let methodName = dashify "_" field.Name
            dashifyRoute $"/{methodName}" >=> fun _ ctx ->
                task {
                    let! arg =
                        task {
                            match argumentTypes with
                            | [| PrimitiveType TypeInfo.Unit|]  ->
                                return [() :> obj]
                            | _ ->
                                // Read arguments from request body
                                let! args = readArguments ctx argumentTypes
                                return args
                        }
                    let! output = method.Invoke arg |> Async.StartAsTask
                    // printfn "output: %A" output
                    let typeInfo = createTypeInfo resultType
                    let json = Convert.serialize output typeInfo

                    ctx.SetContentType "application/json; charset=utf-8"
                    let body = Encoding.UTF8.GetBytes json
                    return! ctx.WriteBytesAsync body
                }
    ])
