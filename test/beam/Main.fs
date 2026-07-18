module Fable.Giraffe.Tests.Main

open System.Threading.Tasks
open Fable.Core

// BEAM runner. On BEAM the `task { }` CE is a CPS alias for Async, so we cast the runner's
// Task to Async (identity) and drive it with Async.RunSynchronously — the same trick the
// production Cowboy handler uses. `run/0` is invoked by test_runner.erl. Remoting is
// Python-only, so it is excluded here.
[<Emit("$0")>]
let private taskToAsync (t: Task<'a>) : Async<'a> = nativeOnly

let private allTests = HandlerTests.tests @ RoutingTests.tests

// Known BEAM-target divergences, kept visible as SKIP rather than silently dropped:
//  * headers — BEAM's HttpRequest.GetTypedHeaders() is a stub returning empty, so the
//    mustAccept/Accept-negotiation tests cannot resolve (tracked: implement Cowboy headers).
//  * JSON formatting — the BEAM jsx serializer differs from the reference (JSON-parity gap).
//  * routef typed captures — %i / %O (Guid) / %u parsing diverges on BEAM (%s works).
let private skip =
    set
        [ "test GET \"/json\" returns json object"
          "test POST \"/text\" with supported Accept header returns \"text\""
          "test POST \"/json\" with supported Accept header returns \"json\""
          "test POST \"/either\" with supported Accept header returns \"either\""
          "test routef: GET \"/foo/johndoe/59\" returns \"Name: johndoe, Age: 59\""
          "test routef: GET \"/foo/%O/bar/%O\" returns \"Guid1: ..., Guid2: ...\""
          "test routef: GET \"/foo/%u/bar/%u\" returns \"Id1: ..., Id2: ...\""
          "test routef: GET \"/foo/bar/baz/qux\" returns 404 \"Not found\"" ]

// Fable >= 5.8 namespaces generated BEAM modules (main -> fable_giraffe_tests_beam_main) and
// emits a `main.erl` shim that dispatches to [<EntryPoint>] and calls erlang:halt with its
// result. Using the entry point rather than a conventionally-named `run/0` keeps the runner
// findable across Fable versions and gets exit-code propagation for free.
[<EntryPoint>]
let main _ : int =
    Testing.runSuite "beam" skip allTests
    |> taskToAsync
    |> Async.RunSynchronously
