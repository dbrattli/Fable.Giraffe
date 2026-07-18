module Fable.Giraffe.Tests.Main

open type Scriptorium.Quill.Runner

// BEAM runner. Quill runs the suite synchronously here and calls `halt/1` with the exit code, so
// `erl` returns non-zero when tests fail. Fable >= 5.8 namespaces generated BEAM modules and
// emits a `main.erl` shim that dispatches to [<EntryPoint>], so using the entry point keeps the
// runner findable across Fable versions. Remoting is Python-only.
[<EntryPoint>]
let main _ =
    runTests [ HandlerTests.tests; RoutingTests.tests ]
