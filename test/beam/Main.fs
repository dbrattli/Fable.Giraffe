module Fable.Giraffe.Tests.Main

open type Scriptorium.Quill.Runner
open type Scriptorium.Quill.Test

// BEAM runner. Quill runs the suite synchronously here and calls `halt/1` with the exit code, so
// `erl` returns non-zero when tests fail. Fable >= 5.8 namespaces generated BEAM modules and emits
// a `main.erl` shim that dispatches to [<EntryPoint>], so the entry point keeps the runner findable
// across Fable versions.
//
// The three suites are wrapped in `testSequenced` so they run one-suite-at-a-time rather than all
// interleaved. Every remoting test passes on its own and the suite passes when run in isolation, but
// under Quill's default cross-suite concurrency on BEAM the remoting body assertions intermittently
// fail with a garbled diff (rendered as "1"). That is a Scriptorium/BEAM concurrency+rendering gap
// tracked in Scriptorium PRs #13 (Quill 0.5.1 Unicode output) and #15 (Nib char-level diffs); once
// those release, drop back to a plain parallel `runTests [ ...; RemotingTests.tests ]`. See #54.
[<EntryPoint>]
let main _ =
    runTests [ testSequenced ("suite", [ HandlerTests.tests; RoutingTests.tests; RemotingTests.tests ]) ]
