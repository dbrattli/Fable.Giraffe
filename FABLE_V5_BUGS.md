# Fable v5 Python Backend Bugs

Bugs discovered while porting Fable.Giraffe to Fable v5. Updated for Fable 5.0.0-rc.6.

## Fixed in 5.0.0-rc.6

### Missing `await` in ternary expressions

Previously, when Fable compiled a two-branch `match` on a boolean into a Python ternary, it generated `await X if cond else Y` where `await` only applied to the true branch. In rc.6, Fable now generates `await (X if cond else Y)` with parentheses so both branches are correctly awaited.

## Fixed in 5.0.0-rc.5

### Missing `await` in if/match branches (statements)

Previously, when an F# function returned `Task<T>` by passing through if/match branches without a `task {}` CE, Fable generated `async def` but omitted `await` on the returned coroutines. This is now fixed — if/match statements correctly generate `await` on both branches.

### `match value with :? Type as x` reassigns outer variable in closure

Previously, using `match value with | :? SomeType as x -> ...` inside a closure would reassign the outer variable in the compiled Python, triggering `UnboundLocalError`. This appears to be fixed (no longer reproducible).

## No known bugs remaining in 5.0.0-rc.6
