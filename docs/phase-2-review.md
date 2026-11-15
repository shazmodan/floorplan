# Phase 2: exit points reviewed by hand

Date: 2026-11-15.

## Fixtures

`tests/golden/effects.exit-points.txt` and `tests/golden/shop.exit-points.txt` are the reviewed results.
Every effect in the fixtures is found, attributed to the module that contains the call (including lambdas,
interface implementations, `async` and `task` bodies), and payloads are recovered as literals or templates
where the code allows. Effects behind Dapper and Flurl.Http are reported at the user call site with the
library named and the call path kept as evidence.

## A real solution

A four-project F# solution that uses FSharp.Compiler.Service, Ionide.ProjInfo, Roslyn and Expecto.

Direct effects (16) are all correct: file reads and writes, `Process.Start` and `Directory` calls, each in the
right module with its payload.

Effects through dependencies were noisy at first. Two causes were fixed:

| Cause | Effect | Fix |
|---|---|---|
| Static constructors linked as ordinary calls | A one-time environment read in a type initializer was charged to every call that touched the type (hundreds of results) | `.cctor` edges are kept for reachability but no longer carry effects (`EdgeKind.Initializer`) |
| `FSharpTypeFunc.Specialize` resolved virtually | Every generic function value in every dependency became reachable from every other | Treated as a function-object invoke, resolved through construction sites only |

What remains: about 90 call sites into FSharp.Compiler.Service symbol properties each reach file reads,
process start, environment and clock primitives. The evidence paths are real (an unresolved entity can raise a
diagnostic whose formatting imports an assembly from disk), but they are 30 to 60 calls deep and describe
error paths, not what the call is for. This is risk 2 in the plan.

## Follow-up for Phase 5 (noise control)

- Give each exit point a confidence derived from the evidence path (length, virtual hops, error paths) and hide
  low-confidence results by default in the website, with a filter to show them.
- Consider a per-assembly cap so that one large dependency cannot dominate the results.
