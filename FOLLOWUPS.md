# Follow-ups

Tracked work deferred out of the target-adaptor refactor (see PR #30). Grouped by area;
roughly ordered by priority within each. These are known gaps/divergences, not regressions.

## Cross-target behavioral divergences

The shared suite (`test/shared/`) now runs on all three backends and surfaced these. Each is
a documented `SKIP` in the relevant per-target runner (`test/<target>/Main.fs`) so it stays
visible until fixed.

- [ ] **JSON serialization parity** — JS (`JSON.stringify`) and BEAM (jsx) emit compact JSON;
      Python's `json.dumps` adds `", "` / `": "` spacing. The `GET /json` case is skipped on
      JS and BEAM. Also blocks a shared `Remoting` (below). Define one JSON contract + add
      union/option round-trip tests across targets. Touches `src/*/Json.fs`.
- [ ] **`routef` typed captures on JS/BEAM** — `%O` (Guid), `%i`, `%u` diverge (`%s` works);
      the affected `routef` cases are skipped on JS and BEAM. Likely `FormatExpressions`
      parsing/conversion differences on those targets. `src/FormatExpressions.fs`.
- [ ] **BEAM `GetTypedHeaders()` is a stub** — returns empty (`RequestHeaders(ResizeArray())`),
      so `mustAccept` / content negotiation can't resolve; the 3 Accept-header cases are
      skipped on BEAM. Implement real Cowboy header reading. `src/beam/HttpContext.fs`.

## BEAM DI / logging

- [x] **BEAM `GetService` across Cowboy's per-request process** — FIXED. Cowboy spawns a fresh
      process per request, and Fable compiles a class with *any* mutable field to a
      process-dictionary ref (a class without one compiles to a plain map — compare the
      generated `service_collection_ctor`, which calls `make_ref`, with `string_values_ctor`,
      which does not). Both the `ServiceCollection` and the `Dictionary` behind it are refs, so
      the collection built in the builder process read back as `undefined` and
      `ctx.GetService<_>()` died with `{badmap,undefined}` → 500.
      `WebHost.Build` now snapshots the services to a `(string * ServiceDescriptor) list` — an
      ordinary term — and `GiraffeHandler` rebuilds a collection from it per request, in the
      process that reads it. Shared `src/Helpers.fs` and `src/HttpContextExtensions.fs` are
      untouched, so Python and JS are unaffected.
- [ ] **BEAM services must be immutable values** — the snapshot above makes the *container*
      portable, not the values in it. A registered service that is itself a class with mutable
      fields is still a dead ref on the far side, failing with the same `{badmap,undefined}`.
      This is inherent to BEAM's shared-nothing model — ETS or a named process would not help,
      since neither can share a mutable *object*. Either document this as the contract
      (records, funs and object expressions are fine) or detect ref-valued services at
      registration and fail loudly at `Build` rather than per request. `src/beam/WebHost.fs`.
- [ ] **BEAM access log bypasses `ILoggerProvider`** — the portable `ILogger` in
      `src/beam/WebHost.fs` writes to OTP `logger` directly, so a custom provider registered
      via `ConfigureLogging` does not receive the access log. Its minimum *level* is honoured
      (`PortableLogger.effectiveLevel` probes `IsEnabled` at `Build`). Behaviourally identical
      for `Fable.Logging.Beam`, which itself emits to OTP `logger`. Resolves if Fable.Logging
      makes its loggers process-portable — see below.
- [ ] **Upstream: make Fable.Logging loggers process-portable on BEAM** — `../Fable.Logging`.
      Two separate asks:
      (a) `Fable.Logging.Beam.Logger` has `member val MinimumLevel with get, set`, but
      `LoggerProvider.CreateLogger` assigns it exactly once right after construction. Making it
      a constructor parameter removes the only mutable field, so the class compiles to a
      portable map. Small, no user-visible API change.
      (b) The factory's own `Logger` mutates a `ResizeArray` of providers from
      `ILoggerFactory.AddProvider`, which is what forces its ref. Fixing this means either
      snapshotting providers at `CreateLogger` time (breaks late `AddProvider`) or documenting
      factory loggers as process-local on BEAM.
      With (a) and (b), `src/beam/WebHost.fs`'s hand-rolled `PortableLogger` can be deleted in
      favour of `loggerFactory.CreateLogger`.
- [ ] **DI test does not cover the process hop** — `test/shared/HandlerTests.fs` now covers
      `AddSingleton` → `ctx.GetService` on all three backends, but it bypasses
      `GiraffeHandler`, so registration and resolution share a process. Covering the real
      Cowboy topology needs a test that drives `GiraffeHandler.init` itself (fake `Req` plus a
      stub for `cowboy_req:reply`), or an integration test against a live listener. Until then
      the cross-process path is verified by running `just app-beam`.

## Feature parity (Python-only today)

- [ ] **`Remoting` → shared** — `src/python/Remoting.fs` is reflection over F# records +
      `HttpHandler` composition + JSON; mostly portable. Blocked on JSON parity above. Once
      unified, move to `src/Remoting.fs` and link from all three fsprojs (like `Core.fs`).
- [ ] **`StaticFiles` per target** — `src/python/StaticFiles.fs` wraps Starlette's static ASGI
      app, so it's inherently Python. Needs per-backend implementations: BEAM → `cowboy_static`,
      JS → Node/Connect static handler (`express.static` / a small `http` handler).

## Adaptor architecture

- [ ] **`IHttpContext` interface (deferred A2)** — an explicit `IHttpContext`/`IHttpRequest`/
      `IHttpResponse` contract that each backend implements, instead of the current duck-typed
      "each target declares a concrete `HttpContext` compiled per-target". Now unblocked: the
      cross-target suite is the safety net that would catch a Fable interface-dispatch codegen
      regression on JS/BEAM. Use a `type HttpContext = IHttpContext` alias so `HttpFunc` text is
      unchanged; carry the `inline` members as interface extensions. Alternatively, keep
      duck-typing + a zero-cost `let inline assertContext` compile-time contract witness.
- [ ] **`HttpHandler` uncurrying fragility** — `HttpFunc -> HttpFunc` is Fable's ambiguous
      "function returning a function" case; the current fix applies handlers fully per request,
      losing Giraffe's precompile-once optimization and breaking if a handler value crosses into
      hand-written JS. Consider a delegate-typed boundary or a Core normalization helper.

## Tooling

- [ ] **Python runner exit code** — Fable Python's emitted `if __name__ == "__main__": main(...)`
      does not propagate `main`'s return as the process exit code, so `test-python` exits 0 even
      on failure (fine today — all green — but CI wouldn't catch a red suite). JS (`process.exit`)
      and BEAM (`erlang:halt(main:run())`) propagate correctly. Have the Python runner
      `sys.exit`/raise on failure. `test/python/Main.fs`.
