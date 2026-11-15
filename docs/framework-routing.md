# Routing inside frameworks: status

Date: 2026-11-15. Results: `tests/golden/frameworks.entry-points.txt`.

Gap 1 of the earlier status was that routing written inside a dependency was not followed. It is now, by a
symbolic interpreter (`src/Floorplan.Analysis/Symbolic.fs`) that knows no framework.

## How it works

1. **Registration phase.** The application's entry point is run over abstract values, through user code, every
   NuGet dependency and FSharp.Core. Closures, records, lists and state machines are objects on a persistent
   heap, and a virtual call dispatches on the concrete type of its receiver, so function values compose exactly
   as they do at run time. What the application registers with the platform is collected: endpoints (from
   endpoint route builders, `RouteEndpointBuilder`, endpoint data sources) and request pipelines (middleware,
   `UseMiddleware<T>`).
2. **Request phase.** Each registered pipeline is run with request data as input. A branch on request data
   forks the path and records the predicate. The first user function reached is the handler; if none is
   reached, the user code that built the library's response is.
3. **Platform knowledge stays in the catalog.** Only ASP.NET Core and BCL facts are used: registrations,
   `HttpMethods.Is*` comparisons and constants, endpoint metadata, `EndpointDataSource.Endpoints` as a platform
   callback, and that a response has not started while a request is routed. Plus platform conventions: async
   builders run their state machine, completed tasks carry their result, framework collections and tuples hold
   what is put in them, pure string functions fold on constants.

Correctness rules that keep it from guessing:

- Conditions checked inside a call are scoped to it. They travel with the call's result (a boolean's true side,
  or a guard on a returned object such as an active pattern's `Some`), not with the path, so an alternative
  that was tried and left does not leak its conditions into the next one.
- A comparison of a value the analysis cannot read with a constant makes the path uncertain when its true side
  is taken; handlers reached on uncertain paths get no route.
- Method names come only from constants or from the strings a sequence actually produces when enumerated.
- Everything is bounded (steps, depth, threads, loop visits); running out loses paths, never invents them.
  User functions handed to the framework but reached by no recovered path are still reported, unresolved.

## Results on the fixtures

Every fixture declares eight routes over all HTTP methods (`GET /`, `GET`/`POST`/`OPTIONS` on a collection,
and `GET`/`PUT`/`DELETE`/`PATCH` on an item, or collection-level `DELETE`/`PATCH` in Giraffe's case).

| Framework | Style | Recovered |
|---|---|---|
| Falco | Endpoint list turned into a custom `EndpointDataSource` | 8 of 8 complete, `/api/orders/{id:int}` from the route template |
| Giraffe | Composed function values run by one middleware | 8 of 8 complete; `routef "/api/orders/%i"` gives `/api/orders/{param}` |
| Oxpecker | Route tree mapped onto endpoint routing, sub-routes as groups | 8 of 8 complete; `routef "/orders/{%i}"` gives `/api/orders/{id}` |

`GET /` is answered by a library function (`text "home"`) in every fixture and is reported as the user code
that set it up. No fixture produces a wrong route. The routing fixtures of Phase 4 are unchanged.

What made the `routef` routes work, all without framework knowledge:

- **Loops with a known end.** A thread that has decided every branch itself (no fork on an unknown value) gets
  the registration phase's loop allowance, so `List.ofSeq` over a 15-character format string finishes
  instead of being cut off at ten iterations.
- **Dictionaries.** `set_Item`, `Add`, `TryAdd`, `get_Item` and `ContainsKey` with constant keys, including
  the `StructBox` F#'s `dict` wraps value-type keys in.
- **Characters.** `c.ToString()` on a char (a constrained call) gives the character, not its code.
- **Regular expressions.** A match's group count is 1 + its capture groups when the match succeeds and 1 when
  it fails, so code that tests `Groups.Count` instead of `Success` still gets the predicate. A regex with a
  known pattern run with an evaluator over a known input (`Regex.Replace`) is executed for real, and each
  match is handed to the evaluator with its group values.
- **Route templates from patterns.** An anchored path pattern made of literal text and capture groups
  (`^/api/orders/(-?\d+)$`) becomes a route template, one parameter per group.
- **Interpolated strings** over constants are folded (`$"{a}{b}"`, the `%s%P()` holes of F#).
- **Reflection over objects the run created:** `GetType`, `GetMethods`, `GetParameters` with declared
  parameter names, and `MethodInvoker.Create`/`Invoke` dispatching to the reflected method.
- **Library responses** are recorded where the response body is written, on a path with certain predicates,
  instead of only when the whole pipeline returns.

## Known limits

- Path explosion is bounded, not solved: frameworks that try alternatives one after another fork at every
  unknown value, and an application with many alternatives can still run out of budget before the last ones.
  The call depth limit is 48, since recursive helpers (one frame per character of a format string) need it.
- A regex route's parameter names are not recovered; the groups are named `{param}` (or `{param1}`, ...).
- The symbolic run only starts for applications that reach a platform registration or request source, so plain
  console applications cost nothing.
- Debugging aids: `FLOORPLAN_DEBUG=1` (or `2`) traces the run; `FLOORPLAN_DEBUG_METHOD=<name>` traces every
  instruction of matching methods.
