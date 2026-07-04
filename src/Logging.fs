namespace Fable.Giraffe

open System

open Fable.Logging

// Shared WebHostBuilder logging wiring. `Fable.Logging` works on every backend, so
// this configuration dance (run the user's callback against the factory, then register
// the "Giraffe" logger as a singleton so handlers can resolve it via GetService) is the
// same for all targets and lives here once. The concrete `ConfigureLogging` member stays
// per-target because it needs the builder's private loggerFactory/services fields —
// it just forwards to this function.
//
// Currently linked by the Python and JS backends. BEAM joins once its DI/services are
// wired into the Cowboy request handler (its GiraffeHandler does not yet set services on
// the context) and it can be verified against the cross-target behavioral suite.
module Logging =

    let configure (loggerFactory: LoggerFactory) (services: ServiceCollection) (configureLogging: Action<ILoggingBuilder>) =
        let loggingBuilder = loggerFactory :> ILoggingBuilder
        configureLogging.Invoke(loggingBuilder)

        let logger = loggerFactory.CreateLogger("Giraffe")
        services.AddSingleton(logger)
