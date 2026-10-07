module Fable.Giraffe.Tests.HttpResponseTests

open System.Text

open Scriptorium.Quill
open Scriptorium.Nib.Assertion
open type Scriptorium.Quill.Test

open Fable.Giraffe

let tests =
    testList (
        "BEAM HttpResponse",
        [
            testAsync (
                "StatusCode property preserves an explicit status when writing the body",
                fun _ ->
                    toAsync (
                        task {
                            let response = HttpResponse()
                            let body = Encoding.UTF8.GetBytes "Created"

                            assertThat response.StatusCode (isEqualTo 404)
                            response.StatusCode <- 201
                            assertThat response.StatusCode (isEqualTo 201)

                            do! response.WriteAsync body

                            assertThat response.StatusCode (isEqualTo 201)
                            assertThat response.Body (isEqualTo body)
                            assertThat response.HasStarted (isEqualTo true)
                        }
                    )
            )
            testAsync (
                "SetStatusCode replaces the property status and preserves it when writing the body",
                fun _ ->
                    toAsync (
                        task {
                            let response = HttpResponse()
                            let body = Encoding.UTF8.GetBytes "Accepted"

                            response.StatusCode <- 201
                            response.SetStatusCode 202
                            assertThat response.StatusCode (isEqualTo 202)

                            do! response.WriteAsync body

                            assertThat response.StatusCode (isEqualTo 202)
                            assertThat response.Body (isEqualTo body)
                            assertThat response.HasStarted (isEqualTo true)
                        }
                    )
            )
        ]
    )
