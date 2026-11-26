# To do

Work that is known and not yet done. Remove an item when its branch is merged.

## Next

In the order they are worth doing: the first ones change results already shown, the later ones add new inputs.

1. **Routes of the owner's real F# application.** The owner's reproduction (demo_project3, 2026-11-26) showed
   the cause: the router is combined with a fallback (`thenTry routing (fun _ -> None)`) and runs inside
   FSharp.Core's `Option.orElseWith`, whose budget as a library call (150,000 steps) it ran out of, so no route
   was found. Fixed on branch `claude/routes-through-library-callbacks` (docs/framework-routing.md, "A router run
   by a library call"; fixture `Routing.Wrapped/Program.fs` now routes through such a `thenTry`). The repro gives
   all 72 routes, identical to its control. Left:
   - run the full suite on that branch (the last run was stopped for low memory; the one before it, with only
     the budget fix, failed only on the fallback reported as an unresolved handler, which the second fix removes;
     no golden should change), then merge, push and delete the branch;
   - the owner runs the real application again with a build from that merge; if routes are still missing,
     another reproduction of what differs.
2. **Folder of solutions, what is left** (the feature is done, docs/landscape-plan.md): a run over both demo
   projects together, once the API demo can be restored (its private package feed is not reachable from this
   machine, so only the admin demo was analyzed, and no link between them was found).

## Later, if wanted (from docs/library-payloads-plan.md)

- A catalog rule that states a library method's payload directly (`"type": "Dapper.SqlMapper"`, ...), for a
  library whose code cannot be followed. Only with the owner's agreement.
- Values kept in caches (`IMemoryCache`, a field holding a dictionary) before use stay dynamic. Collections made
  and read in one method are followed.
- Flurl's URL stays dynamic: it is rendered back from Flurl's parsed `Url` object.

## Left to the owner

- Publishing the dotnet tool to NuGet.
- Choosing a license.
