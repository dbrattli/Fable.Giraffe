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

- [ ] **Behavioral verification of BEAM DI + logging** — the wiring is in place and compiles
      (`GiraffeHandler` now receives `(handler, services)` + `SetServices`; `UseBeamLogging`
      via `Fable.Logging.Beam`), but no test exercises `GetService`. Add a cross-target DI
      test (register a service in each `TestContext.create`, resolve it in a handler) so the
      DI path is actually covered on Python/JS/BEAM. `test/shared/`, `test/*/TestContext.fs`.

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
