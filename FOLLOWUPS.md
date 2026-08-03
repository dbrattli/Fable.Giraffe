# Follow-ups

Tracked work deferred out of the target-adaptor refactor (see PR #30). Grouped by area;
roughly ordered by priority within each. These are known gaps/divergences, not regressions.

## Cross-target behavioral divergences

The shared suite (`test/shared/`) now runs on all three backends and surfaced these. Each is
a documented `SKIP` in the relevant per-target runner (`test/<target>/Main.fs`) so it stays
visible until fixed.

- [x] **JSON serialization parity** — LARGELY FIXED by adopting Fable.TypedJson as the
      serializer. All three backends now derive wire keys from one rule (camelCase), so the
      same record no longer reaches the wire as `Description` on JS and `description` on
      Python/BEAM. `GET /json` runs on all three targets again.
      **Byte-level parity is NOT achieved and is not achievable**: Python's `json.dumps` still
      adds `", "` / `": "` spacing, and Erlang maps have no insertion order, so BEAM emits keys
      in term order (`age, bar, foo`). The `GET /json` expectation is therefore built via
      `serialize` rather than a literal. Compact separators on Python would be a reasonable
      upstream change in `Fable.TypedJson.Python`; key order on BEAM cannot be fixed.
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
- [x] **BEAM access log bypassed `ILoggerProvider`** — FIXED by Fable.Logging 1.0.0. The
      hand-rolled `PortableLogger` wrote to OTP `logger` directly, so a custom provider never
      saw the access log. `Build` now uses `loggerFactory.CreateLogger`, so provider dispatch
      works normally.
- [x] **Upstream: make Fable.Logging loggers process-portable on BEAM** — SHIPPED in
      Fable.Logging 1.0.0 (`feat!: make loggers process-portable on the BEAM`, #40). Both asks
      landed — `Fable.Logging.Beam.Logger` takes its level as a constructor parameter, and the
      factory's `Logger` snapshots providers into an immutable list — plus a
      `LoggerFactory.MinimumLevel` getter. `PortableLogger` and its `IsEnabled`-probing level
      recovery are deleted. Written up in `../Fable.Logging/BEAM-PROCESS-PORTABLE-LOGGERS-PROMPT.md`.
      **Breaking change to respect:** `AddProvider` no longer affects loggers already created,
      so loggers must be created after configuration. `src/Logging.fs` documents where this
      binds.
- [ ] **DI test does not cover the process hop** — `test/shared/HandlerTests.fs` now covers
      `AddSingleton` → `ctx.GetService` on all three backends, but it bypasses
      `GiraffeHandler`, so registration and resolution share a process. Covering the real
      Cowboy topology needs a test that drives `GiraffeHandler.init` itself (fake `Req` plus a
      stub for `cowboy_req:reply`), or an integration test against a live listener. Until then
      the cross-process path is verified by running `just app-beam`.

## Feature parity (Python-only today)

- [x] **`Remoting` → shared** — DONE earlier; and its hand-rolled argument reconstruction
      (`convertJsonValue`, `MakeRecord` per field, `'T list` recursion) is now gone too,
      replaced by one TypedJson codec per argument built from the reflected `System.Type`.
      That removed the second implementation of the type walk, the per-backend key mapping
      (BEAM's `toWireKey`), and the limitation that unions, options and maps passed through
      unconverted.
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
