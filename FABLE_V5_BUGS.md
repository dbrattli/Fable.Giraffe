# Fable v5 Python Backend Bugs

Bugs discovered while porting Fable.Giraffe to Fable v5. Updated for Fable 5.0.0-rc.5.

## Fixed in 5.0.0-rc.5

### Missing `await` in if/match branches (statements)

Previously, when an F# function returned `Task<T>` by passing through if/match branches without a `task {}` CE, Fable generated `async def` but omitted `await` on the returned coroutines. This is now fixed — if/match statements correctly generate `await` on both branches.

### `match value with :? Type as x` reassigns outer variable in closure

Previously, using `match value with | :? SomeType as x -> ...` inside a closure would reassign the outer variable in the compiled Python, triggering `UnboundLocalError`. This appears to be fixed (no longer reproducible).

## Still broken in 5.0.0-rc.5

### Missing `await` in ternary expressions

**Severity:** Critical
**Affects:** Python backend

When Fable compiles a simple two-branch `match` on a boolean into a Python ternary expression, only one branch gets `await`. This happens when the function returns `Task<T>` directly (pass-through) without a `task {}` CE.

**F# input:**

```fsharp
let compose (handler1: HttpHandler) (handler2: HttpHandler) : HttpHandler =
    fun (final: HttpFunc) ->
        let func = final |> handler2 |> handler1
        fun (ctx: HttpContext) ->
            match ctx.Response.HasStarted with
            | true -> final ctx
            | false -> func ctx
```

**Generated Python (incorrect):**

```python
async def _arrow(ctx):
    return await final(ctx) if has_started(ctx) else func(ctx)  # BUG: missing await on else branch
```

**Expected Python:**

```python
async def _arrow(ctx):
    return await final(ctx) if has_started(ctx) else await func(ctx)
```

**Pattern:** Two-branch `match` on a boolean that Fable optimizes into a ternary expression. Multi-statement if/match blocks (which Fable emits as full `if/else` statements) are now correctly awaited.

**Workaround:** Wrap in explicit `task { return! ... }` to force the task builder path, which avoids the raw ternary:

```fsharp
fun (ctx: HttpContext) -> task {
    match ctx.Response.HasStarted with
    | true -> return! final ctx
    | false -> return! func ctx
}
```
