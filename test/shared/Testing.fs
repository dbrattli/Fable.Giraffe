namespace Fable.Giraffe.Tests

open System.Threading.Tasks

open Fable.Giraffe

// Zero-dependency, cross-target test harness (the house pattern used by Fable.Logging /
// Fable.Reactive): pure-F# assertions that raise on failure, an inert `Fact` attribute so
// existing [<Fact>] annotations still parse, and per-module `tests` lists that every
// backend's runner (native .NET / Python / JS / BEAM) executes uniformly. No xUnit, no
// pytest, no Expecto — those don't port across the Fable targets.
[<AutoOpen>]
module Testing =

    /// Assert equality; raises with a readable message on mismatch. Call convention matches
    /// the old shim: `actual |> equal expected`.
    let equal (expected: 'T) (actual: 'T) : unit =
        if expected <> actual then
            failwithf "Expected %A but got %A" expected actual

    let notEqual (expected: 'T) (actual: 'T) : unit =
        if expected = actual then
            failwithf "Expected values to differ but both were %A" expected

    /// Terminal HttpFunc used to drive a handler pipeline in tests.
    let next: HttpFunc = Some >> Task.FromResult

    /// Read the response Content-Type header (first value).
    let getContentType (response: HttpResponse) = response.Headers["Content-Type"][0]

    /// Inert stand-in so existing `[<Fact>]` annotations compile on every target.
    type FactAttribute() =
        inherit System.Attribute()

    /// Shared cross-target runner: run each case (awaiting its task), print PASS/FAIL, and
    /// loudly SKIP any name in `skip` (a documented, target-specific known divergence — kept
    /// visible rather than silently dropped). Returns a Promise/Task of the exit code, which
    /// the caller blocks on (Python/.NET) or awaits (JS). BEAM runs it via Async.
    let runSuite (target: string) (skip: Set<string>) (tests: (string * (unit -> Task<unit>)) list) : Task<int> =
        task {
            printfn "Fable.Giraffe behavioral tests (%s target)" target
            let mutable passed = 0
            let mutable ran = 0

            for (name, f) in tests do
                if skip.Contains name then
                    printfn "  SKIP  %s (known %s divergence)" name target
                else
                    ran <- ran + 1

                    try
                        do! f ()
                        printfn "  PASS  %s" name
                        passed <- passed + 1
                    with ex ->
                        printfn "  FAIL  %s\n        %s" name ex.Message

            let skipped = List.length tests - ran
            printfn ""
            printfn "%d/%d passed (%d skipped)" passed ran skipped
            return (if passed = ran then 0 else 1)
        }
