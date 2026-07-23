module Fable.Giraffe.Tests.Main

open type Scriptorium.Quill.Runner

// Python target runner. Quill runs the suite synchronously here (Async.RunSynchronously under
// the hood), so the returned exit code is the process exit code.
[<EntryPoint>]
let main _ =
    runTests [ HandlerTests.tests; RoutingTests.tests; RemotingTests.tests ]
