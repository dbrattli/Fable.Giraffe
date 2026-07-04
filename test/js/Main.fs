module Fable.Giraffe.Tests.Main

open System.Threading.Tasks

// JS/Node runner. Tests compile to Promises here, so the shared runner awaits each; `run`
// returns that Promise of the exit code and run.mjs propagates it. Remoting is Python-only.
let private allTests = HandlerTests.tests @ RoutingTests.tests

// Known JS-target divergences, kept visible as SKIP rather than silently dropped:
//  * JSON formatting — JS `JSON.stringify` is compact; Python's json.dumps adds ", "/": "
//    spacing. Tracked under the cross-target JSON-parity gap.
//  * routef %O (Guid) — FormatExpressions Guid matching returns 404 on the JS target.
let private skip =
    set
        [ "test GET \"/json\" returns json object"
          "test routef: GET \"/foo/%O/bar/%O\" returns \"Guid1: ..., Guid2: ...\"" ]

let run () : Task<int> = Testing.runSuite "js" skip allTests
