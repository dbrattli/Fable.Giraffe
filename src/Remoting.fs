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

let route (path: string) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let segment = SubRouting.getNextPartOfPath ctx |> dashify "_"
        if segment.Equals path then
            next ctx
        else
            skipPipeline ()

// let inline json<'T> (dataObj: Async<string>) : HttpHandler =
//     fun (_: HttpFunc) (ctx: HttpContext) ->
//         let output = Async.RunSynchronously dataObj
//         let json = Json.serialize dataObj
//         let bytes = Encoding.UTF8.GetBytes json
//         ctx.SetContentType "application/json; charset=utf-8"
//         ctx.WriteBytesAsync bytes

let removeNamespace (fullName: string) =
    fullName.Split('.')
    |> Array.last
    |> (fun name -> name.Replace("`", "_"))

type Method0<'A, 'B> = 'A -> Async<'B>

// let dynamicallyInvoke (methodName: string) implementation methodArg =
//      let propInfo = implementation.GetType().GetProperty(methodName)
//      // A -> Async<B>, extract A and B
//      let propType = propInfo.PropertyType
//      let fsharpFuncArgs = propType.GetGenericArguments()
//      // A
//      let argumentType = fsharpFuncArgs.[0]
//      if (argumentType <> methodArg.GetType()) then
//         let expectedTypeName = argumentType.Name
//         let providedTypeName = methodArg.GetType().Name
//         let errorMsg = sprintf "Expected method argument of '%s' but instead got '%s'" expectedTypeName providedTypeName
//         failwith errorMsg
//      // Async<B>
//      let asyncOfB = fsharpFuncArgs.[1]
//      // B
//      let typeBFromAsyncOfB = asyncOfB.GetGenericArguments().[0]
//
//      let boxer = typedefof<AsyncBoxer<_>>.MakeGenericType(typeBFromAsyncOfB)
//                  |> Activator.CreateInstance
//                  :?> IAsyncBoxer


     // let fsAsync = FSharpRecord.Invoke (methodName, implementation, methodArg)

     // async {
     //    let! asyncResult = boxer.BoxAsyncResult fsAsync
     //    return asyncResult
     // }


let inline fromValue (api: 'T)  () =
    let typ = api.GetType()
    let fullname = typ.FullName
    let apiName = removeNamespace fullname

    let fields = FSharpType.GetRecordFields typ
    //printfn "fields: %A" fields

    subRoute $"/{apiName}" (choose [
        for field in fields do
            printfn "field: %A" field
            let value = field.GetValue api

            let propType = field.PropertyType
            printfn "Propertytype: %A" typ
            // A -> Async<B>, extract A and B
            let fsharpFuncArgs = propType.GetGenericArguments()
            // A
            let argumentType = fsharpFuncArgs.[0]
            printfn "ArgumentType: %A" argumentType
            // if (argumentType <> methodArg.GetType()) then
            //     let expectedTypeName = argumentType.Name
            //     let providedTypeName = methodArg.GetType().Name
            //     let errorMsg = sprintf "Expected method argument of '%s' but instead got '%s'" expectedTypeName providedTypeName
            //     failwith errorMsg
            // Async<B>
            let asyncOfB = fsharpFuncArgs.[1]
            // B
            let typeBFromAsyncOfB = asyncOfB.GetGenericArguments().[0]
            printfn "typeBFromAsyncOfB: %A" typeBFromAsyncOfB

            let method = (value :?> Method0<_, _>)
            printfn "value: %A" value

            let fieldName = dashify "_" field.Name

            route $"/{fieldName}" >=> fun _ ctx ->
                task {
                    // Read arguments from request body
                    let! arg =
                        task {
                            match argumentType with
                            | PrimitiveType TypeInfo.Unit ->
                                return () :> obj
                            | _ ->
                                let! json = ctx.ReadBodyFromRequestAsync()
                                let inputJson = SimpleJson.parseNative json
                                let typeInfo = (fun _ -> createTypeInfo argumentType) |> TypeInfo.List
                                let args = Convert.fromJson<List<_>> inputJson typeInfo
                                return args.[0] :> obj
                        }
                    let! output = method arg |> Async.StartAsTask
                    printfn "output: %A" output
                    let typeInfo = createTypeInfo (typeBFromAsyncOfB)
                    printfn "TypeInfo: %A" typeInfo
                    let js = Convert.serialize output typeInfo
                    printfn "js: %A" js

                    ctx.SetContentType "application/json; charset=utf-8"
                    let body = Encoding.UTF8.GetBytes js
                    return! ctx.WriteBytesAsync body
                }
    ])
