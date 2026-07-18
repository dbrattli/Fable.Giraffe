namespace Fable.Giraffe.Tests

open System.Threading.Tasks

open Fable.Giraffe

// Shared plumbing for the cross-target behavioral suite. Assertions and the runner come from
// Scriptorium (Nib + Quill); what is left here is the glue the suite needs to drive Giraffe
// handlers and to hand a `task` to Quill, which speaks `Async`.
[<AutoOpen>]
module Helpers =

    /// Terminal HttpFunc used to drive a handler pipeline in tests.
    let next: HttpFunc = Some >> Task.FromResult

    /// Read the response Content-Type header (first value).
    let getContentType (response: HttpResponse) = response.Headers["Content-Type"][0]

    /// Bridge a handler pipeline's Task into the Async that Quill's `testAsync` expects.
    let toAsync (t: Task<'a>) : Async<'a> = Async.AwaitTask t
