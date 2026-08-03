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
// Linked by all three backends. BEAM's GiraffeHandler sets the services collection on the
// context, so handlers there resolve the "Giraffe" logger via GetService like everywhere else.
module Logging =

    /// Apply the caller's logging configuration, then (re)register the "Giraffe" logger.
    ///
    /// The order matters as of Fable.Logging 1.0.0: a logger snapshots the factory's providers
    /// and minimum level when it is created, and `AddProvider` no longer reaches back into
    /// loggers already handed out (that mutable state is what made them process-local on BEAM).
    /// Creating the logger AFTER the callback is therefore required, and re-registering on every
    /// ConfigureLogging call is what keeps the singleton current when a host chains several —
    /// e.g. `.ConfigureLogging(SetMinimumLevel Debug).UseStructlog()`, where the provider only
    /// arrives on the second call. Last call wins, and it has seen all configuration so far.
    let configure (loggerFactory: LoggerFactory) (services: ServiceCollection) (configureLogging: Action<ILoggingBuilder>) =
        let loggingBuilder = loggerFactory :> ILoggingBuilder
        configureLogging.Invoke(loggingBuilder)

        let logger = loggerFactory.CreateLogger("Giraffe")
        services.AddSingleton(logger)
