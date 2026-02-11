# Fable v5 Python Backend Bugs

Bugs discovered while porting Fable.Giraffe to Fable 5.0.0-alpha.23. These should be investigated and fixed in the Fable compiler or fable-library.

## Bug 1: Missing `await` in async functions returning Task from if/match branches

**Severity:** Critical
**Affects:** Python backend

When an F# function returns `Task<T>` by passing through another task from if/match branches (without using a `task {}` CE), Fable generates `async def` but omits `await` on the returned coroutines.

**F# input:**

```fsharp
type HttpFuncResult = Task<HttpContext option>
type HttpFunc = HttpContext -> HttpFuncResult

let skipPipeline () : HttpFuncResult = Task.FromResult None

let httpVerb (validate: string -> bool) =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        if validate ctx.Request.Method then
            next ctx
        else
            skipPipeline ()
```

**Generated Python (incorrect):**

```python
async def httpVerb(validate, next_1, ctx):
    if validate(get_method(ctx)):
        return next_1(ctx)        # BUG: returns coroutine object, not result
    else:
        return skipPipeline()     # BUG: same issue
```

**Expected Python:**

```python
async def httpVerb(validate, next_1, ctx):
    if validate(get_method(ctx)):
        return await next_1(ctx)   # Should await
    else:
        return await skipPipeline() # Should await
```

**Also affects ternary expressions:**

```python
# Generated:
return await final(ctx) if condition else func(ctx)  # Only one branch gets await

# Expected:
return await final(ctx) if condition else await func(ctx)
```

**Pattern:** Functions that return `Task<T>` directly (pass-through) without `task {}` CE. Functions with statements before the return (e.g., `do_something(); next ctx`) correctly get `await`.

**Workaround:** Wrap in explicit `task { return! ... }`:

```fsharp
fun (next: HttpFunc) (ctx: HttpContext) -> task {
    if validate ctx.Request.Method then
        return! next ctx
    else
        return! skipPipeline ()
}
```

## Bug 2: `match value with :? Type as x` reassigns outer variable in closure

**Severity:** Medium
**Affects:** Python backend

When using type test pattern `match value with | :? SomeType as x -> ...` inside a closure, the compiled Python reassigns the outer `value` variable instead of creating a new binding. This triggers Python's `UnboundLocalError` due to closure scoping rules.

**F# input:**

```fsharp
let process (value: obj) =
    task {
        if key = "body" then
            match value with
            | :? (byte array) as bytes -> body.Add bytes
            | _ -> failwith "expected byte array"
    }
```

**Generated Python (incorrect):**

```python
def _arrow(value=value):
    if key == "body":
        if isinstance(value, Array):     # UnboundLocalError!
            value = cast(Array, value)   # This assignment shadows outer 'value'
```

**Workaround:** Avoid `match ... with :? T as x` pattern. Use direct cast instead:

```fsharp
if key = "body" then
    body.Add(value :?> byte array)
```
