module Fable.Giraffe.Tests.Main

// Python target runner: aggregate every module's `tests` list and run them via the shared
// runner. Python can block, so we drive the runner task to completion synchronously.
let private allTests =
    HandlerTests.tests
    @ RoutingTests.tests
    @ RemotingTests.tests

// Python matches the reference behaviour, so nothing is skipped here.
let private skip: Set<string> = Set.empty

[<EntryPoint>]
let main _ =
    (Testing.runSuite "python" skip allTests).GetAwaiter().GetResult()
