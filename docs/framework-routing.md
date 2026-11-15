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

| Framework | Style | Recovered |
|---|---|---|
| Falco | Endpoint list turned into a custom `EndpointDataSource` | All four routes complete, including `GET /` answered by a library function |
| Giraffe | Composed function values run by one middleware | `GET /api/orders` complete; `POST /api/orders` and `routef` handler found, unresolved |
| Oxpecker | Route tree mapped onto endpoint routing, sub-routes as groups | `GET /` complete; the three handlers under `subRoute` found, unresolved |

No fixture produces a wrong route. The routing fixtures of Phase 4 are unchanged.

## Known limits

- Path explosion: frameworks that try alternatives one after another (Giraffe's `choose`) fork at every unknown
  value, and later alternatives can run out of budget before they are reached. Giraffe's `routef` builds a
  regular expression from the format string at request time, which is expensive to interpret.
- Oxpecker's nested route groups lose their templates on some paths; the handlers are reported without routes.
- The symbolic run only starts for applications that reach a platform registration or request source, so plain
  console applications cost nothing.
- Debugging aids: `FLOORPLAN_DEBUG=1` (or `2`) traces the run; `FLOORPLAN_DEBUG_METHOD=<name>` traces every
  instruction of matching methods.
