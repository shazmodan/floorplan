# Payloads through libraries: plan

Date: 2026-11-17. Status: in progress. Extends plan section 5, step 5 ("Recover payloads").

## Resuming work (read this first)

This plan is worked in iterations. Each one is a branch of its own, ends with every test green, and can be merged
on its own. To pick up after a break:

1. Read the **Status** table below. The first iteration not marked done is the current one.
2. Check `git branch -a` for that iteration's branch. If it exists, check it out and read its commits and the
   iteration's **Log** entry: that is where work stopped. If not, branch from `main` with the name in the table.
3. Do the iteration's steps, then its **Done when** checks and the whole suite:
   `dotnet run --project tests/Floorplan.Tests -- --summary` (as CI runs it; `--filter-test-list "Library payloads"`
   for this work's tests). Goldens are updated with `FLOORPLAN_UPDATE_GOLDEN=1`; check every changed line.
4. Before committing, update this file in the same commit: the Status row, and a dated **Log** entry with what was
   done, what was found, and anything left over. Findings that change the design go into **Design** as well.
5. Ask the owner before merging; after the merge, delete the branch.

Never leave knowledge only in a conversation: if a session ends mid-iteration, the Log entry (committed on the
branch, or at least saved in the working tree) must say where it stopped.

## Status

| # | Iteration | Branch | Status |
|---|---|---|---|
| 1 | Wrapper fixture and pending tests | `claude/library-payload-wrapper-fixture` | done |
| 2 | Arguments passed straight through, with caching | `claude/library-payload-pass-through` | done |
| 3 | Values built inside the library | `claude/library-payload-built-values` | done |
| 4 | Properties, fields, constructors, returned objects | `claude/library-payload-object-members` | not started |
| 5 | Async and iterator state machines | `claude/library-payload-state-machines` | not started |
| 6 | Dapper | `claude/library-payload-dapper` | not started |
| 7 | Other paths when the shortest gives nothing | `claude/library-payload-other-paths` | not started |
| 8 | Flurl and HTTP request objects (stretch) | `claude/library-payload-http-requests` | not started |
| 9 | Documentation and TODO | with the last iteration merged | not started |

## Goal

A call into a dependency that reaches a primitive with a payload (SQL, a URL, a path, a process name) shows that
payload at the user's call site, worked out from the dependency's own code. No rule names the library: Dapper, an
in-house wrapper and any other library are handled the same way. What cannot be followed stays "dynamic", as
today; nothing is guessed.

Not goals: catalog rules per library (possible later as a fallback, see the end); following values through
collections, caches, reflection or virtual calls with several targets.

## Where things are today

- `src/Floorplan.Analysis/Summaries.fs`: for each dependency method, every rule it reaches with the shortest
  witness (`Reaches`), and the call path to the primitive (`Path`). Only one witness per (method, rule).
- `src/Floorplan.Analysis/ExitPoints.fs`, the `Dependency` branch: one exit point per category, from the shortest
  witness; `Payload = None` always.
- `src/Floorplan.Analysis/Values.fs`: `Evaluator` over one method's `DataFlow.Flow` gives `Text`, `Hole` or
  `Parts`. `PropertyPayload` follows `set_<Property>` on the receiver and a constructor argument, in one method.
- `src/Floorplan.IL/DataFlow.fs`: values are `Pushed offset`, `Argument index` (0 is `this`), `LocalAddress`,
  `CaughtException`. Arguments are already distinguished, which is the start of everything below.
- `src/Floorplan.Analysis/EffectAnalysis.fs`: dependency summaries are cached per (assembly, MVID, catalog), key
  `summaries-v2`. A cached dependency is not re-read, so anything payloads need from inside it must be cached too.
- Goldens today: Dapper's database exit is `IDbConnection::Open` (the shortest path; it has no payload), Flurl's
  is `HttpClient::SendAsync` whose payload is a request object (role `request`).

## Design

### Origins

The payload of a primitive, seen from a method on the path, is an **origin**: what the value is in terms of that
method's arguments. As built (`src/Floorplan.Analysis/Values.fs`), it is a flat list of segments, concatenated, so
that it caches as plain records (the cache's JSON writes no unions with fields):

```fsharp
type Segment =
    | Literal of string                              // text fixed inside the library
    | Unknown                                        // something it cannot follow
    | FromParameter of index: int * members: string list
        // argument i (0 is `this` for instance methods), then a property or field path into it (iteration 4)
type Origin = Segment list
```

Field names are normalized to the property they back (`<CommandText>k__BackingField` and F#'s `CommandText@`
become `CommandText`), so a property set in one place and a field read in another meet.

### Walking the path backwards

For a witness path `M1 → M2 → … → Mk → primitive` (from `Summaries.Path`):

1. In `Mk`, the payload operand of the primitive call (from the rule's `PayloadSpec`: an argument, or a property of
   the receiver) is resolved to an origin in terms of `Mk`'s arguments.
2. Going up one step, `Parameter i` of `Mj+1` is replaced by the origin of operand `i` at the call site in `Mj`,
   and `Member(o, name)` is resolved where `o` was made (below).
3. At the user's call site, the remaining `Parameter i` is the call's operand `i`, which the existing `Evaluator`
   turns into literal, template or dynamic. `Unknown` anywhere gives dynamic.

Resolving a value in a method, to an origin (an extension of `Evaluator.one`):

- `ldstr`, string concatenation, `string.Format`, interpolation: as today, but holes that are arguments become
  `Parameter`, not `Hole`.
- `Argument i` → `Parameter i`.
- A getter `get_X` or a field load on a value `r` → `Member(origin r, X)`.
- `Member(o, X)` where `o` was made in this method: the last `set_X` or field store of `X` on that object before
  the use (as `PropertyPayload`), or else what its constructor stores into `X` (look into the constructor body:
  its store to `X` resolves to a `Parameter` of the constructor, then to that constructor call's operand).
- The return value of a call to a dependency method `C` → `C`'s **return origin**: what its `ret` returns, in terms
  of its arguments; `Member(returned, X)` looks in `C` for what it sets on the object it returns.
- Anything else (a call into the base libraries other than the transparent ones, a load from a collection, a
  static field, several possible producers) → `Unknown`.

Every recursion is bounded (depth and a per-run memo keyed by method and origin), as `Values` is today.

### Choosing what to show

In the `Dependency` branch of `ExitPoints`, per category: if any witness's rule has a payload spec and its origin
resolves to a literal or template at this call site, that witness is used, its primitive and evidence path
included, so the evidence shows where the payload went. Otherwise, as today: the shortest witness, no payload, or a
dynamic payload when the chosen rule has a payload spec. (Decided in this plan; revisit in iteration 6 if the
goldens read badly.)

### Caching

Origins are computed per dependency method and rule, in terms of that method's arguments, so they are a property of
the dependency alone and are cached with its summaries: each cached witness gets an optional origin, the cache key
becomes `summaries-v3`. A cached run must give the same payloads as an uncached one; every iteration tests that.
Bump the version in each iteration that changes how origins are computed.

### Compiler output

The state machines of async methods and iterators copy arguments into fields: the starting stub stores them into
the new state machine (`stfld`), `MoveNext` reads them through `this`. That is the `Member` rule above. Iterators add
a second copy (`<>3__x` into `x` in `GetEnumerator`), which is Roslyn's own and goes into
`ICodegenRecognizer` (`src/Floorplan.IL/Codegen.fs`) as a mapping from a state machine field to the argument it
holds; F# has its own recognizer for its closures and state machines.

## Iterations

### 1. Wrapper fixture and pending tests

A C# library referenced as a built assembly (as `tests/fixtures/CSharp/Dependency/CSharp.Library` is, see
`CSharp.Delegated.csproj`), at `tests/fixtures/CSharp/Dependency/CSharp.Wrappers`, called from
`CSharp.Effects/ThroughWrappers.cs`. One method per case, with its expected payload:

| Case | Call site (`ThroughWrappers`) | Library code (`Sql`, `Http`) | Expected |
|---|---|---|---|
| a | `ExecLiteral` | `Exec(conn, sql)`: `cmd.CommandText = sql; cmd.ExecuteNonQuery()` | the literal at the call site |
| b | `FetchLiteral` | `Fetch(client, url)` → `client.GetStringAsync(url)` | the literal |
| c | `RunChain` | a chain: `Run(conn, sql)` → `Exec(conn, sql)` | the literal |
| d | `CountLiteral`, `CountDynamic` | `Count(conn, table)`: `"SELECT count(*) FROM " + table` | the full text for a literal, template `SELECT count(*) FROM {…}` for an unknown |
| e | `ScalarReturned` | `Scalar` calls private `Prepare(conn, sql)`, which returns the command; `Scalar` executes it | the literal |
| f | `ExecuteQueryObject` | the caller makes `new Query(sql)`; `Execute(conn, query)` reads `query.Text` | the literal |
| g | `ExecAsyncLiteral` | `ExecAsync(conn, sql)` (async) | the literal |
| h | `RowsLiteral` | `Rows(conn, sql)` (iterator, `yield return`) | the literal |
| i | `ThroughDictionary` | the SQL goes through a `Dictionary` before use | dynamic |
| j | `Interpolated` | `Exec(conn, sql)` called with an interpolated string | template |
| k | `ExecuteDefinition` | makes a `Definition` struct from `sql`, passes it by `in` to a private method that sets `CommandText` from it (Dapper's shape) | the literal |

Tests in a new `tests/Floorplan.Tests/LibraryPayloadTests.fs`, one per case, written now as pending (`ptest`) and
enabled by the iteration that makes them pass. Plus: the fixture builds for net8.0 and net10.0; the existing
goldens are unchanged except for the new call sites. **Done when**: all tests green, the new ones pending.

### 2. Arguments passed straight through, with caching

`Origin` with `Literal`, `Unknown`, `FromParameter` (no members yet); the backward walk along the witness path; the
choice in `ExitPoints`; origins in the summaries cache (`summaries-v3`; `summaries-v4` from iteration 3). **Done when**: cases a, b, c, i pass (`LibraryPayloadTests.fs`); a test runs
the fixture twice with a cache store and gets the same payloads both times.

### 3. Values built inside the library

The `Evaluator` producing an argument, not a hole, inside concatenations and templates. **Done when**: cases d and j
pass.

### 4. Properties, fields, constructors, returned objects

`Member`, field name normalization, constructor bodies, return origins. **Done when**: cases e, f and k pass.

### 5. Async and iterator state machines

The `stfld`/`ldfld` path through `this` of `MoveNext`, and the recognizer mapping for Roslyn iterators. Check F#
`task { }` and `seq { }` wrappers as well, with a small F# case if the C# ones pass. **Done when**: cases g and h
pass.

### 6. Dapper

First trace `Execute(cnn, sql)`, then `Query<T>(cnn, sql)`, in the existing Effects fixtures (F#
`Effects/Effects.App/Wrapped.fs`, C# `CSharp/CSharp.Effects/Wrapped.cs`). Expected route in Dapper's source:
`Query` builds a `CommandDefinition` struct from `sql`, passes it to the iterator `QueryImpl`, which calls
`command.SetupCommand(cnn, …)` (creates the command, sets `CommandText` from the struct) and then executes the
command. Add a C# `Execute` call to the fixture. Write down in the Log which step fails, if any, with the IL that
causes it. **Done when**: `queryNames`/`QueryNames` show `SELECT name FROM users` in the goldens, or the Log says
exactly why not and the case is left pending.

### 7. Other paths when the shortest gives nothing

`Summaries` keeps one witness per (method, rule): the shortest. If its path ends in `Unknown` (a cache lookup, for
example), try the method's other callees that also reach the rule, bounded in count and depth. Only if iteration 6
or the fixture shows the need; otherwise mark it skipped.

### 8. Flurl and HTTP request objects (stretch)

`HttpClient.SendAsync(request)` has the payload role `request`. Give the catalog the URL as a property of the request
(`RequestUri`, the payload form from the SQL work) and see how far Flurl's `Url` object gets. Expect dynamic; record
the reason.

### 9. Documentation and TODO

`docs/csharp-support.md` (remove the Dapper limit), plan section 5 step 5, README if it mentions payload limits, and
this file's status. Each earlier iteration updates `docs/csharp-support.md` only if what it adds is visible to users.

## Fallback, not planned

If a widely used library cannot be followed, a catalog rule could state the payload of a library method directly
(`"type": "Dapper.SqlMapper", "members": ["Query*"], "payload": [{ "argument": 1, "role": "sql" }]`), shipped or
passed with `--catalog`. Only after iteration 6, and only with the owner's agreement.

## Log

(Dated entries, newest last: what was done, found, and left over.)

**2026-11-17, iteration 1, done.** Added `tests/fixtures/CSharp/Dependency/CSharp.Wrappers` (no packages, only
the base libraries), referenced by `CSharp.Effects` as a built assembly, and `CSharp.Effects/ThroughWrappers.cs`
with cases a to k (k added: a struct passed by `in`, Dapper's shape). `tests/Floorplan.Tests/LibraryPayloadTests.fs`
has one active test (every case is reported via `CSharp.Wrappers`) and the cases as `ptest`, grouped by the
iteration that enables them. `tests/golden/csharp.exit-points.txt` gained the twelve call sites; all reach the
expected primitive already (for example `ExecuteNonQueryAsync` for g, `ExecuteReader` for h), with no payload.
Next: iteration 2 from `main` once this is merged.

**2026-11-17, iteration 2, done.** Built as designed:
- `Values.fs`: `Segment`/`Origin`; `Evaluator.OriginOf` (an argument, or else today's evaluation with holes as
  `Unknown`), `Substitute` (a callee's origin seen from a call in a library method), `Resolve` (at the user's call
  site, into a `Symbolic`), and `PropertySource` (what `PropertyPayload` already did, returning the values).
- `Summaries.fs`: `PayloadOf(m, ruleId)`, memoized per (method, rule), walking the stored witness edge by edge;
  only `Direct` and `Virtual` edges are followed, `Deferred` and `StateMachine` give `Unknown` (iteration 5).
  `DependencyEffect.Payload` carries the role and origin. The constructor takes the recognizers.
- `EffectAnalysis.fs`: `CachedWitness.Payload` (`CachedPayload` of `CachedSegment` records), key `summaries-v3`.
- `ExitPoints.fs`: per category, the shortest witness whose payload resolves to a literal or template, else the
  shortest; a rule with a payload spec now shows "dynamic" instead of nothing.
- Tests a, b, c, i pass, and j (interpolated argument) already did: nothing more was needed. New test: a warm cache
  gives the same payloads, and the cache files hold origins with parameters.
- Goldens: the wrapper cases as expected; `CountLiteral` and `CountDynamic` both show the template
  `SELECT count(*) FROM {…}` (the argument inside a concatenation is still a hole: iteration 3 makes the literal
  one full text); Flurl's `SendAsync` changed from no payload to `dynamic` (role `request`, not followed); Dapper
  unchanged (`IDbConnection::Open`, the `IDbCommand` witness is still unknown).
Next: iteration 3.

**2026-11-17, iteration 3, done.** `Values.Symbolic` has a new case `Param i`: the incoming value of argument
`i`, which `Evaluator` now gives for an argument instead of `Hole`. Everywhere else it reads as a hole (all other
matches on `Symbolic` fall through to a wildcard; alone, it is still a dynamic payload). `Origin.ofSymbolic` turns
it into `FromParameter`, so `"SELECT count(*) FROM " + table` is `[Literal; FromParameter 2]`, and `OriginOf` is
just that. `ToPayload` now gives a literal for parts that are all text (it gave a template without holes). Cache key
`summaries-v4`. Tests d (both) pass; one golden line changed: `CountLiteral` is `literal:SELECT count(*) FROM
orders`. No change in any other golden: direct calls that join constant strings did not occur in the fixtures.
Next: iteration 4.
