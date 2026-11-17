# Payloads through libraries: plan

Date: 2026-11-17. Status: done (all iterations merged). Extends plan section 5, step 5 ("Recover payloads").

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
5. Ask the owner before merging; after the merge, delete the branch. (2026-11-17: the owner asked to merge, push
   and go on without asking until the plan is fully implemented.)

Never leave knowledge only in a conversation: if a session ends mid-iteration, the Log entry (committed on the
branch, or at least saved in the working tree) must say where it stopped.

## Status

| # | Iteration | Branch | Status |
|---|---|---|---|
| 1 | Wrapper fixture and pending tests | `claude/library-payload-wrapper-fixture` | done |
| 2 | Arguments passed straight through, with caching | `claude/library-payload-pass-through` | done |
| 3 | Values built inside the library | `claude/library-payload-built-values` | done |
| 4 | Properties, fields, constructors, returned objects | `claude/library-payload-object-members` | done |
| 5 | Async and iterator state machines | `claude/library-payload-state-machines` | done |
| 6 | Dapper | `claude/library-payload-dapper` | done |
| 7 | Other paths when the shortest gives nothing | done in iteration 6 (Dapper needed it) | done |
| 8 | Flurl and HTTP request objects (stretch) | `claude/library-payload-http-requests` | done (Flurl stays dynamic) |
| 9 | Documentation and TODO | `claude/library-payload-docs` | done |

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

(As built from iteration 6 on, the choice of route happens first inside the dependency, per method: see the log of
iteration 6. What follows still holds at the user's call site.)

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
choice in `ExitPoints`; origins in the summaries cache (`summaries-v3`; `summaries-v4` from iteration 3, `summaries-v5` from iteration 4, `summaries-v7` from iteration 5, `summaries-v8` from iteration 6, `summaries-v9` from iteration 8). **Done when**: cases a, b, c, i pass (`LibraryPayloadTests.fs`); a test runs
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

**2026-11-17, iteration 4, done.** In `Values.fs`:
- `Param` carries a member path (`Param(i, ["Text"])`: property `Text` of argument `i`), as `FromParameter` does.
- `Evaluator` reads instance getters (`get_X`) and field loads as members of a value (`memberValue`): the last
  store to that member on the same object before the read (a setter, a field store, or a constructor called on a
  local struct or on `this`); else, for an argument, `Param(i, [X])`; for an object from `newobj` or from a call,
  what that constructor or method sets on it; for a member of a member, the path grows. Fields are named after
  the property they back (`memberOfField`). A getter that gives nothing falls back to the old evaluation.
- `Producers` (one per run, in `EffectAnalysis.Program`, passed to `Summaries` and to `ExitPoints` through
  `ProjectContext.Producers`) reads what a constructor stores into the new object, or what a method sets on the
  one object it returns (single `ret` only), memoized per (method, member); base-library methods are not read.
  `Evaluator` takes it as its fourth argument; the three-argument constructor (entry points, route recovery)
  has none.
- `ReceiverMember` replaces `PropertySource` for property payloads: the member as above, then the constructor
  argument fallback (`constructorArgument`, for constructors whose body sets a differently named field, such as
  Npgsql's). `Substitute` is now `Origin.ofSymbolic` of `Resolve`, and `Resolve` follows member paths.
- Found: a constructor's call to `System.Object::.ctor` comes after its field stores and looked like the last
  store to `this`; constructor calls now count as stores only when the producers know something about them.
- Cache key `summaries-v5`. Tests e, f, k pass; golden changes only those three lines. F# backing fields (`X@`)
  are normalized but not covered by a fixture yet.
Next: iteration 5.

**2026-11-17, iteration 5, done.**
- `ICodegenRecognizer.StateMachineArgumentFields` (C#: `x` → `<>3__x`, the iterator starter's copy; F#: none).
- `Evaluator.ThroughStateMachine`: a `StateMachine` edge's callee origin is in terms of its `this`; the machine is
  the object constructed at the edge (a class: `newobj`, Debug builds), the `initobj` local, or the starter's local
  of the machine's type (a struct, optimized builds, edge offset 0). Its fields are what the starter stores; for a
  field the starter did not store, the recognizer's alternative names are tried.
- `Deferred` edges (function objects) go the same way, but only for an object constructed at the edge: F# `seq { }`
  compiles to a class constructed with the arguments, its `GenerateNext` linked by the F# recognizer. C# lambdas
  (a display class made elsewhere) stay unknown.
- Found: in a state machine, a local is a field of `this`, loaded anew at each use, so the setter's receiver and the
  executing call's receiver were different values. `memberValue` now matches receivers by identity
  (`identity`: a local by number, a field by field name and owner, else the value).
- `CSharp.Wrappers` is built optimized (`<Optimize>true</Optimize>`), as packages are: its async state machine is
  a struct started through the builder. Both builds were checked: Debug (class) passed before the switch.
- F# check: new `tests/fixtures/Effects/Dependency/Effects.Wrappers` (F#, optimized), called from
  `Effects.App/ThroughWrappers.fs`: an argument, a record field (`Text@` backing field), `task { }` (inlined
  into the method by the compiler, nothing more needed) and `seq { }`; all four give their SQL (one test).
- Cache key `summaries-v7` (v6 was never committed). Goldens: g and h in `csharp.exit-points.txt`, four new lines
  in `effects.exit-points.txt`; nothing else changed (Dapper still `IDbConnection::Open`).
Next: iteration 6, Dapper.

**2026-11-17, iteration 6 (and 7), done.** Dapper's `Query` and `Execute` show their SQL in both fixtures (new
call sites `DeleteSessions`/`deleteSessions` calling `Execute`; one test).
- `Execute`: the shortest witness was `ExecuteImpl`'s multi-row loop, where `CommandText` is reset from a local
  holding several values (unknown). The single-command route is one call longer:
  `ExecuteImpl` → `ExecuteCommand` → `SetupCommand` (sets `CommandText` from the `CommandDefinition` passed by
  reference) → `IDbCommand.ExecuteNonQuery`. So iteration 7 was needed and is done here: `Summaries.Choose`, per
  (method, rule with a payload), tries the method's calls that reach the rule (the primitive, or a method reaching
  it), shortest first, at most 8, and keeps the first whose origin is not entirely unknown; else the shortest.
  `Reaches` and `Path` use that witness, so the evidence, the primitive and the cache follow the payload's route
  (Dapper's `queryNames` is now `IDbCommand::ExecuteReader` instead of `IDbConnection::Open`).
- `Query`: `Query` builds the `CommandDefinition` on a local (`ldloca; call .ctor`), passes it by value to the
  iterator `QueryImpl`, which stores it into `<>3__command`; `MoveNext` passes `this.command` to `SetupCommand` by
  field address (`ldflda`), keeps the command in a hoisted field `<cmd>5__2`, and hands it to
  `ExecuteReaderWithFlagsFallback`, which executes it. Needed: field addresses read as members (`LdFldA`, also in
  `identity`), and a member of a value read from a member (a field, a field address, a getter) resolved through
  what was stored into that member (`lastStore`, used by `memberValue`).
- Cache key `summaries-v8`. Golden changes: the four Dapper lines (two new, two from `Open` to the command with
  its SQL); the Flurl lines only moved with the new call sites.
Next: iteration 8, Flurl.

**2026-11-17, iteration 8, done.** A payload can now be a property of an argument: `{ "argument": 0, "property":
"RequestUri", "constructorArgument": 1, "role": "url" }` (`Evaluator.OperandMember`; `ReceiverMember` is the
receiver's case). `HttpClient.Send*` and `HttpMessageInvoker.Send*` use it, so their role is `url` instead of
`request`; the constructor fallback takes a `Uri` parameter as well as a string. New wrapper cases
`RequestConstructed` (`new HttpRequestMessage(method, url)`) and `RequestProperty` (`RequestUri = new Uri(url)`)
show their URLs (one test). Flurl stays dynamic, as expected: `FlurlClient.SendAsync` builds the message from
`Url.op_Implicit(request.Url)`, which renders Flurl's parsed `Url` object (scheme, host, path segments, query)
back into a string; that is not followed. Cache key `summaries-v9`. Golden: the two new lines (the others moved).
Next: iteration 9, documentation.

**2026-11-17, iteration 9, done.** `docs/csharp-support.md` (the Dapper limit replaced by what is followed and what
is not), plan section 5 step 5 and section 4.1 (done in iteration 8), `TODO.md` (item removed). The README does not
describe payloads. Left for later, if wanted: the catalog-rule fallback above (not needed for Dapper); C# lambdas
in a library (a display class made elsewhere) stay unknown; values through collections or caches stay dynamic.
