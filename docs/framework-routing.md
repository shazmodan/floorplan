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
  that was tried and left does not leak its conditions into the next one. A `bool` function that returns a
  constant `true` after narrowing the request returns those conditions as its true side.
- A comparison of a value the analysis cannot read with a constant makes the path uncertain when its true side
  is taken; handlers reached on uncertain paths get no route.
- Method names come only from constants or from the strings a sequence actually produces when enumerated.
- Everything is bounded (steps, depth, threads, loop visits); running out loses paths, never invents them.
  User functions handed to the framework but reached by no recovered path are still reported, unresolved, if
  their signature takes or returns a request type (the catalog's request-context and request-source types).
  Configuration callbacks handed to a library at startup (logging, services, options) are not handlers.

## Results on the fixtures

Every fixture declares eight routes over all HTTP methods (`GET /`, `GET`/`POST`/`OPTIONS` on a collection,
and `GET`/`PUT`/`DELETE`/`PATCH` on an item, or collection-level `DELETE`/`PATCH` in Giraffe's case).

| Framework | Style | Recovered |
|---|---|---|
| Falco | Endpoint list turned into a custom `EndpointDataSource` | 8 of 8 complete, `/api/orders/{id:int}` from the route template |
| Giraffe | Composed function values run by one middleware | 8 of 8 complete; `routef "/api/orders/%i"` gives `/api/orders/{id}` |
| Oxpecker | Route tree mapped onto endpoint routing, sub-routes as groups | 8 of 8 complete; `routef "/orders/{%i}"` gives `/api/orders/{id}` |
| Saturn | Routers as computation expressions on Giraffe, nested with `forward`, run by the generic host | 8 of 8 complete, including the OPTIONS route forwarded after the whole orders router |

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
  (`^/api/orders/(-?\d+)$`) becomes a route template, one parameter per group. The groups are named after the
  handler's parameters that are not part of the request pipeline (`get (id: int) : HttpHandler` gives `{id}`),
  when there are exactly as many of them as groups.
- **Interpolated strings** over constants are folded (`$"{a}{b}"`, the `%s%P()` holes of F#).
- **Reflection over objects the run created:** `GetType`, `GetMethods`, `GetParameters` with declared
  parameter names, and `MethodInvoker.Create`/`Invoke` dispatching to the reflected method.
- **Library responses** are recorded where the response body is written, on a path with certain predicates,
  instead of only when the whole pipeline returns.

What Saturn added:

- **The generic host.** Delegates taking `Microsoft.AspNetCore.Hosting` types (`ConfigureWebHostDefaults`,
  `Configure`) are called back during registration, like those taking pipeline types.
- **Dictionaries with structural keys** (tuples, by their parts; other objects by identity), entries enumerated
  as key-value pairs in insertion order, and `TryGetValue` writing its out argument into the caller's local.
- **Per-request storage.** `HttpContext.Items` is one dictionary per path, empty when the request arrives.
- **The rest of a path.** A substring of the path from the length of a prefix the request is known to start with
  is the rest of the path after it; comparing it with a constant compares the whole path with the prefix put
  back (`StartsWith "/orders"` after `/api` is `StartsWith "/api/orders"`, and likewise for equality and
  anchored patterns). Any other substring of the path is unknown: before this, it kept the path's tag, and a
  comparison with it could have been reported as a comparison of the whole path.
- **Decided tests and impossible paths.** A path test the known predicates already decide (a path starting with
  `/api/orders` contains `/api`) is a constant, and predicates that cannot hold together (two prefixes neither
  of which extends the other, an exact path without a required prefix) end the path.
- **Known request values.** An equality test on a request part whose value the path already fixed (`method =
  "GET"` earlier, `match method with "GET"` later) is decided the same way.
- **Path segments.** `segments.Length` compared with a constant gives the segment count, and a segment passed
  to the handler without being compared is a route parameter, named after the handler's parameter. With these,
  the hand-written router of `Routing.Middleware` moved into a library (`Routing.Delegated`, routing in
  `tests/fixtures/Routing/Dependency`) gives the same routes as in user code; a test checks that.
- **Equivalent outcomes** of a call (same result, predicates, last user code and contents of
  `HttpContext.Items`) continue as one, so the alternatives of a `choose` do not multiply into each other. Two
  more rules keep a failed alternative from returning several "no match" outcomes: an outcome whose result's
  guard contradicts itself or the path is dropped, and so is one whose result only adds guard conditions to
  another's on the same path. Outcomes on different paths are never merged by inclusion: a failed test leaves
  no predicate, so fewer predicates do not mean a more general path.
- **Handled requests end their callers.** A call none of whose paths returns yields an unknown value (so that a
  throw does not hide the alternatives after it), except when its paths ended by reaching a handler: then the
  request is handled and the calling paths end too, instead of trying every later alternative again.
- **Tests on unreadable strings.** `StartsWith`, `EndsWith` and `Contains` on a string the run could not read,
  while a request is routed, make their true side uncertain, so a handler behind them gets no route. Before,
  such a test forked without a trace, and a handler behind it could get a route that left the test out.
- **FSharp.Core's own sequences.** Closures nested under `<StartupCode$FSharp-Core>` (the `seq { }` expressions
  of `Seq.append` and similar) are interpreted; only FSharp.Core's static initialization is skipped. Before,
  every such sequence enumerated as unknown and forked at each step.

## Known limits

- Path explosion is bounded, not solved: frameworks that try alternatives one after another fork at every
  unknown value, and an application with many alternatives can still run out of budget before the last ones.
  The call depth limit is 96 (nested routers and recursive helpers need it); the interpreter runs on its own
  thread with a 512 MB stack so that depth cannot overflow the caller's. A call into a dependency gets at most
  150,000 steps, shared with the calls inside it, so an alternative tried after a large one can still be left
  without budget in a bigger application than the fixtures.
- `Substring(n, length)` of the path with an unknown length is taken as the rest of the path from `n`; that is
  how slices compile, but a slice of a middle piece would be read the same way.
- A regex route whose handler takes its values some other way (one tuple, or more parameters than groups) keeps
  the placeholder names `{param}` (or `{param1}`, ...).
- The symbolic run only starts for applications that reach a platform registration of an HTTP handler: what it
  reports comes from such registrations alone. An application that reads requests in a loop of its own (an
  `HttpListener`, a socket) routes them in its own code, which the static scan reads; running its endless loop
  spent the whole step budget (1.5 million steps for `CSharp.Switch` and `Routing.Records`) for nothing.
- The models are tested one at a time in `tests/Floorplan.Tests/SymbolicTests.fs`, over the functions of
  `SymbolicSamples.fs` compiled into the test assembly.
- Debugging aids: `FLOORPLAN_DEBUG=1` (or `2`) traces the run; `FLOORPLAN_DEBUG_METHOD=<name>` traces every
  instruction of matching methods.
