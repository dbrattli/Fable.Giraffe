module Fable.Giraffe.Remoting

open System
open System.Text

open FSharp.Reflection
open Fable.SimpleJson.Python

let createApi () = ()

let dashifyRoute (path: string) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let segment = SubRouting.getNextPartOfPath ctx |> dashify "_"
        if segment.Equals path then next ctx else skipPipeline ()

// Signature of the function that will be called by the client. Needs to be uncurried.
[<RequireQualifiedAccess>]
type Signature<'A, 'B, 'C, 'TResult> =
    | Arity0 of 'TResult
    | Arity1 of Func<'A, 'TResult>
    | Arity2 of Func<'A, 'B, 'TResult>
    | Arity3 of Func<'A, 'B, 'C, 'TResult>

    member x.Invoke(args: List<obj>) =
        match x with
        | Arity0 f -> f
        | Arity1 f -> f.Invoke(args[0] :?> _)
        | Arity2 f -> f.Invoke(args[0] :?> _, args[1] :?> _)
        | Arity3 f -> f.Invoke(args[0] :?> _, args[1] :?> _, args[2] :?> _)

    static member Create(value: obj, arity: int) =
        match arity with
        | 0 -> Arity0 (value :?> _)
        | 1 -> Arity1 (value :?> Func<_, _>)
        | 2 -> Arity2 (value :?> Func<_, _, _>)
        | 3 -> Arity3 (value :?> Func<_, _, _, _>)
        | _ -> failwith "Only methods with 1, 2 or 3 arguments are supported"

let readArgumentsFromBodyAsync (ctx: HttpContext) (argumentTypes: Type array) = task {
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

let getFunctionTypes (funcType: Type) (param: Reflection.PropertyInfo) =
    let isFunctionType (t: Type) =
        t.GetGenericTypeDefinition() = typedefof<FSharpFunc<_, _>>

    let isAsyncType (t: Type) =
        t.GetGenericTypeDefinition() = typedefof<Async<_>>

    if not ((isFunctionType funcType) || (isAsyncType funcType)) then
        failwithf $"Bad API record field %s{param.Name}, must be of type Async<'a> or a function returning Async<'a>"

    // Uncurry the function argments
    let rec uncurry (t: Type) =
        printfn "uncurry: %A" t
        match t with
        | _ when isFunctionType(t) -> t.GetGenericArguments() |> Array.collect uncurry
        | _ when isAsyncType(t) -> [| t.GetGenericArguments()[0] |]
        | _ -> [| t |]
    uncurry funcType

let inline fromValue (api: 'T) () =
    let typ = api.GetType()
    let apiName = removeNamespace typ.FullName
    let fields = FSharpType.GetRecordFields typ

    subRoute
        $"/{apiName}"
        (choose [
            for field in fields do
                let value = field.GetValue api

                let propType = field.PropertyType
                let functionTypes = getFunctionTypes propType field
                let argumentTypes =
                    if functionTypes.Length > 1 then
                        functionTypes[0 .. functionTypes.Length - 2]
                    else
                        [||]

                let resultType = functionTypes[functionTypes.Length - 1]
                let method = Signature.Create<_, _, _, _>(value, argumentTypes.Length)
                let methodName = dashify "_" field.Name

                dashifyRoute $"/{methodName}"
                >=> fun _ ctx -> task {
                        let! args = task {
                            match argumentTypes with
                            | [||]
                            | [| PrimitiveType TypeInfo.Unit |] -> return [ () :> obj ]
                            | _ -> return! readArgumentsFromBodyAsync ctx argumentTypes
                        }
                        let! output = method.Invoke args |> Async.StartAsTask
                        let json = createTypeInfo resultType |> Convert.serialize output

                        ctx.SetContentType "application/json; charset=utf-8"
                        let body = Encoding.UTF8.GetBytes json
                        return! ctx.WriteBytesAsync body
                    }
        ])
