# Phase 0 spike: findings

Date: 2026-11-15. Code: `spike/` (throwaway, not part of the product build).

The spike answers risks 1 to 3 in section 11 of the implementation plan.

## Risk 1: can route conditions be recovered from IL?

`spike/RouterSample/Router.fs` contains three differently written routers:

1. A list pattern combined with `GET`/`POST` active patterns.
2. `StartsWith` and `String.Equals` control flow.
3. A `Dictionary<string, unit -> string>` lookup.

`spike/IlProbe` dumps their IL with Mono.Cecil, with PDB lines.

| Router | What the IL shows | Recoverable? |
|---|---|---|
| List pattern | `FSharpList.get_HeadOrDefault` followed by `ldstr "api"` and `String.Equals(string,string)`, then `brfalse` to the next case. Each `get_TailOrNull` + `brfalse`/`brtrue` gives the length check. A segment bound to a name (`id`) is a `HeadOrDefault` stored to a local and passed to the handler | Yes: the segment index is the number of `TailOrNull` calls on the path from the list value |
| Active pattern | `call |GET|_|(Req)` then `brfalse` on the option. The constant `"GET"` lives inside the active-pattern body, compared to `Req.Method` | Yes, by entering the active-pattern body and reading its own predicate (an inter-procedural predicate summary: "returns Some iff arg.Method = "GET"") |
| `StartsWith` | `ldarg; ldstr "/api/orders"; callvirt String::StartsWith(string); brfalse` | Yes, trivially |
| `Dictionary` | `Dictionary.set_Item(ldstr "/api/orders", <closure or method delegate>)` at construction; `TryGetValue(path)` at lookup | Yes for form B when both sites are in analyzed code |

Handlers are always the first user call on the branch (`call Orders::list()` with a sequence point), so handler discovery does not depend on predicate recovery.

**Decision: go** for form A with a stack-based abstract interpreter over IL basic blocks, tracking *tagged values* (request data) and *constants*. Branch conditions are recorded when `brtrue`/`brfalse` consume the result of a recognized comparison.

## Risk 2: does the call graph over-approximate?

Closures are constructed with `newobj <Enclosing>/<name>@<line>::.ctor(...)` and passed as `FSharpFunc` values. Linking construction sites to the closure's `Invoke` body (rather than resolving `FSharpFunc.Invoke` virtually to every closure in the program) keeps closures precise. `async { }` compiles to a closure passed to `AsyncBuilder.Delay`, which the same rule covers.

Interface and virtual calls are resolved with rapid type analysis (only instantiated types are candidates). `System.Object` virtuals are never resolved.

**Decision: go**, with the rules above.

## Risk 3: is analysis fast enough on real dependency trees?

Scanning every method body of FSharp.Compiler.Service, FSharp.Core and Mono.Cecil (about 97,000 methods and 346,000 call sites, a deliberately large dependency set) with Cecil took about 3.5 s on the development machine, cold, single-threaded.

That is the whole-assembly cost; the product only analyzes methods reachable from user code and caches dependency summaries per MVID.

**Decision: go.** Reading with Cecil is not the bottleneck. A per-assembly summary cache (Phase 5) is still worth having.

## Consequences for the implementation

- Keep Mono.Cecil behind `IAssemblyReader` (open decision 2 stays open; no reason to switch).
- F#-specific recognizers needed for the first version: list head/tail access, option `get_Value`/null checks, closure construction, `FSharpFunc.Invoke`, active-pattern calls.
- The route-recovery fallback (reading conditions from the F# syntax tree) is not needed.
