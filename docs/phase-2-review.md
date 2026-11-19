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

## A C# solution: modular-monolith-with-ddd

Date: 2026-11-18. kgrzybek/modular-monolith-with-ddd: .NET 8, 25 projects analyzed, Dapper for queries, EF Core
for writes, SqlStreamStore, Quartz, Autofac and Serilog. Built with `NuGetAudit=false` (the solution treats the
IdentityServer4 audit warning as an error), analyzed with `--no-build`.

### Payloads

| Effect | Calls | Payload |
|---|---|---|
| Dapper (`Query*`, `Execute*`, sync and async) | 109 | all 109 literal SQL |
| SqlStreamStore, dbup | 5 | dynamic or none: the SQL is the library's own, read from embedded scripts |
| Connection opens in the solution's own factory | 2 | none (the rule has no payload) |
| Outgoing HTTP | 0 | the solution makes no HTTP calls |
| Files (Quartz, Serilog sinks, the solution's startup) | about 20 | literal where configured in code (`logs/logs`, `quartz.config`), else dynamic |

The SQL comes through whole: multi-line `SELECT`s with `nameof` columns, `INSERT`s into the outbox and inbox
tables. Before this review one call was dynamic: the solution's `PagedQueryHelper.AppendPageStatement(sql)`
appends a paging clause to the SQL it is given, as an interpolated string with two `const` holes. A value returned
by a method that builds it from its arguments is now followed, and so are strings formatted into an interpolated
string, so the call shows its whole SQL ending in `OFFSET @Offset ROWS FETCH NEXT @Next ROWS ONLY;`.

### Fixed

| Found | Fix |
|---|---|
| 70 file writes through Serilog named `File::Open` as the primitive while the evidence ended at `File::Delete` | A rule covers several members. The witness through a callee now takes the primitive from the callee's chosen route (the one whose payload is known), not its shortest one; the cache stores the same. |
| One Dapper call's SQL dynamic | Returned values of the solution's (and dependencies') methods are followed, and `AppendFormatted(string)` parts of interpolated strings, as above. |

### Not followed

- **EF Core writes.** `SaveChangesAsync` (two call sites) reaches no database primitive: EF Core runs its commands
  through services resolved from its own container, which the call graph does not follow. A catalog rule for
  `DbContext.SaveChanges*` would name them; this is the "catalog rule fallback" in `TODO.md`.

### Noise

- **Logging (fixed 2026-11-19).** Every Serilog call (83) reached the rolling file sink's `File::Delete` and
  `Directory::Exists`, the console sink and the clock, through `ILogEventSink.Emit`. The paths are real and the
  sinks are configured, but one result per log call says nothing about the call. File reads and writes were shown
  by default at medium confidence: 166 of the 294 exit points the site showed by default. Log calls are now
  catalog rules of a new kind, `logger` (Serilog's `ILogger` and `Log`), and what a unit's log calls reach is one
  exit point per effect, shown at the unit's first log call, with every log call in its evidence; an entry point
  reaching any of them has it in its trace. Configuring the logger (`WriteTo.File("logs/logs")`) stays an exit point of
  its own. After: Serilog gives 85 exit points instead of 341 (one per effect for each of the 24 units that log, and
  9 `ForContext` calls reaching the clock), 38 of them shown by default instead of 166; the site shows 166 exit
  points by default instead of 294.
- **Library internals.** Dapper (`Guid.NewGuid` naming its generated methods), Autofac (callback ids) and Quartz
  report nondeterminism at every call. These categories are hidden by default.
