module Fable.Giraffe.Remoting

open System
open System.Text

open FSharp.Reflection

open Fable.Giraffe
open Fable.Giraffe.Json

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
        | 0 -> Arity0(value :?> _)
        | 1 -> Arity1(value :?> Func<_, _>)
        | 2 -> Arity2(value :?> Func<_, _, _>)
        | 3 -> Arity3(value :?> Func<_, _, _, _>)
        | _ -> failwith "Only methods with 0, 1, 2 or 3 arguments are supported"

/// Body shape written when a call fails: `{"error": "..."}` on every backend.
type RemotingError = { error: string }

module RemotingHelpers =
    /// Rebuild the value a handler expects from a JSON-decoded argument.
    ///
    /// Records are reconstructed field-by-field from the reflection TypeInfo and recursed into,
    /// so nested records and lists of records arrive as real records rather than raw
    /// dicts/objects. Anything else (scalars, unions, options) is passed through untouched —
    /// see the limitations note in CLAUDE.md.
    let rec convertJsonValue (targetType: Type) (value: obj) : obj =
        let genericDef (t: Type) =
            try
                Some(t.GetGenericTypeDefinition())
            with _ ->
                None

        if isNull value then
            value
        elif
            FSharpType.IsRecord targetType
            && isJsonObject value
        then
            let fields = FSharpType.GetRecordFields targetType

            let values =
                fields
                |> Array.map (fun f -> convertJsonValue f.PropertyType (getJsonMember value f.Name))

            FSharpValue.MakeRecord(targetType, values)
        else
            match genericDef targetType with
            | Some def when def = typedefof<_ list> ->
                let elementType = targetType.GetGenericArguments()[0]

                value :?> obj seq
                |> Seq.map (convertJsonValue elementType)
                |> List.ofSeq
                |> box
            | _ -> value

    let dashifyRoute (path: string) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let segment = SubRouting.getNextPartOfPath ctx |> dashify "_"

                if segment.Equals path then
                    return! next ctx
                else
                    return! skipPipeline ()
            }

    /// Decode the request body into the handler's argument list, or describe why it could not be.
    /// The failure is returned rather than raised so the caller can answer 400 instead of 500.
    let readArgumentsFromBodyAsync (ctx: HttpContext) (argumentTypes: Type array) =
        task {
            let! json = ctx.ReadBodyFromRequestAsync()

            let decoded =
                try
                    Ok(deserialize json :?> obj seq |> Seq.toArray)
                with _ ->
                    Error $"Expected a JSON array of %d{argumentTypes.Length} argument(s)"

            match decoded with
            | Error message -> return Error message
            | Ok args when args.Length <> argumentTypes.Length ->
                return Error $"Expected %d{argumentTypes.Length} argument(s) but got %d{args.Length}"
            | Ok args ->
                try
                    return
                        args
                        |> Array.mapi (fun i arg -> convertJsonValue argumentTypes[i] arg)
                        |> Array.toList
                        |> Ok
                with _ ->
                    return Error "Arguments did not match the expected types"
        }

    let getFunctionTypes (funcType: Type) (param: Reflection.PropertyInfo) =
        let isFunctionType (t: Type) =
            t.GetGenericTypeDefinition() = typedefof<FSharpFunc<_, _>>

        let isAsyncType (t: Type) =
            t.GetGenericTypeDefinition() = typedefof<Async<_>>

        if
            not (
                (isFunctionType funcType)
                || (isAsyncType funcType)
            )
        then
            failwithf $"Bad API record field %s{param.Name}, must be of type Async<'a> or a function returning Async<'a>"

        // Uncurry the function argments
        let rec uncurry (t: Type) =
            match t with
            | _ when isFunctionType t -> t.GetGenericArguments() |> Array.collect uncurry
            | _ when isAsyncType t -> [| t.GetGenericArguments()[0] |]
            | _ -> [| t |]

        uncurry funcType

    /// Write a JSON body with the given status code.
    let private writeJson (ctx: HttpContext) (statusCode: int) (json: string) =
        ctx.SetStatusCode statusCode
        ctx.SetContentType "application/json; charset=utf-8"
        ctx.WriteBytesAsync(Encoding.UTF8.GetBytes json)

    let createRoutes api apiName (errorHandler: (exn -> HttpHandler) option) (fields: Reflection.PropertyInfo array) =
        subRoute
            $"/{apiName}"
            (choose
                [ for field in fields do
                      let value = field.GetValue api

                      let propType = field.PropertyType
                      let functionTypes = getFunctionTypes propType field

                      let argumentTypes =
                          if functionTypes.Length > 1 then
                              functionTypes[0 .. functionTypes.Length - 2]
                          else
                              [||]

                      let method = Signature.Create<_, _, _, _>(value, argumentTypes.Length)
                      let methodName = dashify "_" (field.Name.TrimEnd('_'))

                      dashifyRoute $"/{methodName}"
                      >=> fun next ctx ->
                          task {
                              let! argsResult =
                                  task {
                                      match argumentTypes with
                                      | [||] -> return Ok [ () :> obj ]
                                      | [| t |] when t = typeof<unit> -> return Ok [ () :> obj ]
                                      | _ -> return! readArgumentsFromBodyAsync ctx argumentTypes
                                  }

                              match argsResult with
                              | Error message -> return! writeJson ctx 400 (serialize { error = message })
                              | Ok args ->
                                  // Async.Catch rather than try/with around a `let!`: it is the
                                  // construct Fable compiles consistently across backends.
                                  let! outcome = method.Invoke args |> Async.Catch |> startAsTask

                                  match outcome with
                                  | Choice1Of2 output ->
                                      ctx.SetContentType "application/json; charset=utf-8"
                                      return! ctx.WriteBytesAsync(Encoding.UTF8.GetBytes(serialize output))
                                  | Choice2Of2 ex ->
                                      match errorHandler with
                                      | Some handler -> return! handler ex next ctx
                                      | None -> return! writeJson ctx 500 (serialize { error = "Internal server error" })
                          } ])

type ProtocolImplementation<'context, 'serverImpl> =
    | Empty
    | StaticValue of 'serverImpl

type RemotingOptions<'context, 'serverImpl> =
    { Implementation: ProtocolImplementation<'context, 'serverImpl>
      RouteBuilder: string -> string -> string
      ErrorHandler: (exn -> HttpHandler) option }

let createApi () =
    { Implementation = Empty
      RouteBuilder = sprintf "/%s/%s"
      ErrorHandler = None }

let fromValue (api: 'T) (options: RemotingOptions<_, 'T>) =
    { options with
        Implementation = StaticValue api }

/// Defines how routes are built using the type name and method name. By default, the generated routes are of the form `/typeName/methodName`.
let withRouteBuilder builder options = { options with RouteBuilder = builder }

/// Handle exceptions raised by an API method. Without one, a failing method answers
/// 500 with `{"error": "Internal server error"}` and the exception is not leaked to the client.
let withErrorHandler (handler: exn -> HttpHandler) options =
    { options with
        ErrorHandler = Some handler }


let inline buildHttpHandler (options: RemotingOptions<_, 'T>) =
    let api =
        match options.Implementation with
        | Empty -> failwith "No API implementation provided"
        | StaticValue api -> api

    let typ = api.GetType()
    let apiName = removeNamespace typ.FullName
    let fields = FSharpType.GetRecordFields typ

    RemotingHelpers.createRoutes api apiName options.ErrorHandler fields
