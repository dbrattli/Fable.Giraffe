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
    let dashifyRoute (path: string) : HttpHandler =
        fun (next: HttpFunc) (ctx: HttpContext) ->
            task {
                let segment = SubRouting.getNextPartOfPath ctx |> dashify "_"

                if segment.Equals path then
                    return! next ctx
                else
                    return! skipPipeline ()
            }

    /// <summary>
    /// Decode the request body into the handler's argument list, or describe why it could not be.
    /// The failure is returned rather than raised so the caller can answer 400 instead of 500.
    /// </summary>
    /// <remarks>
    /// Each argument is decoded by a TypedJson codec built from its reflected
    /// <c>System.Type</c>. This used to be a hand-rolled walk here — reconstructing records
    /// field-by-field via <c>MakeRecord</c> and recursing through <c>'T list</c>, looking each
    /// field up by its pristine F# name. That was a second implementation of the type walk the
    /// serializer already performs, and it agreed with the wire only by convention: it passed
    /// unions, options and maps through unconverted, and needed a per-backend key mapping
    /// (BEAM's <c>toWireKey</c>) to find fields at all. One codec per argument replaces all of it,
    /// and the key derivation is now shared with encode by construction.
    /// </remarks>
    let readArgumentsFromBodyAsync (ctx: HttpContext) (argCodecs: Fable.TypedJson.Json.TypedJson<obj> array) =
        task {
            let! json = ctx.ReadBodyFromRequestAsync()

            let decoded =
                try
                    Ok(deserialize json :?> obj seq |> Seq.toArray)
                with _ ->
                    Error $"Expected a JSON array of %d{argCodecs.Length} argument(s)"

            match decoded with
            | Error message -> return Error message
            | Ok args when args.Length <> argCodecs.Length ->
                return Error $"Expected %d{argCodecs.Length} argument(s) but got %d{args.Length}"
            | Ok args ->
                // Fold rather than map so the first bad argument short-circuits with its own
                // field-level errors, which are far more actionable than "did not match".
                let folded =
                    Array.zip argCodecs args
                    |> Array.fold
                        (fun acc (codec, arg) ->
                            match acc with
                            | Error _ -> acc
                            | Ok soFar ->
                                match codec.decode arg with
                                | Ok value -> Ok(value :: soFar)
                                | Error errs -> Error(Fable.TypedJson.Schema.formatErrors errs))
                        (Ok [])

                return folded |> Result.map List.rev
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

                      let returnType = functionTypes[functionTypes.Length - 1]

                      dashifyRoute $"/{methodName}"
                      >=> fun next ctx ->
                          task {
                              // Codecs are built per request, not hoisted out of this lambda.
                              // A codec closes over arrays, which Fable compiles to ref-backed
                              // structures on BEAM, so one built in the builder process reads
                              // back as `undefined` inside Cowboy's per-request process. The
                              // shared test suite would not catch it — it never crosses a
                              // process boundary — so this is load-bearing and not an oversight.
                              let responseCodec = codecFor returnType
                              let argCodecs = argumentTypes |> Array.map codecFor

                              let! argsResult =
                                  task {
                                      match argumentTypes with
                                      | [||] -> return Ok [ () :> obj ]
                                      | [| t |] when t = typeof<unit> -> return Ok [ () :> obj ]
                                      | _ -> return! readArgumentsFromBodyAsync ctx argCodecs
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
                                      return! ctx.WriteBytesAsync(Encoding.UTF8.GetBytes(responseCodec.encode output))
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
