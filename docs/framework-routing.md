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

## Routing in a pipeline the application wrote

Found on an application (2026-11-23) whose every route was unresolved: ASP.NET Core middleware of its own,
`app.Use((context, next) => ...)`, maps the request to a package's request record, calls the routing function
it was handed (`routing (mapRequest context)`) and writes the response. The router matches the package's active
patterns (`GET (["Roles"; Int32Str _; _], _)`), with or-patterns sharing one response and the path's prefix
taken off before a nested router. Request data tracking cannot follow a function value, and the interpreter took
the middleware, the first user function it reached, as the handler. Fixture: `Routing.Wrapped`, with the
package's shapes in `Dependency/Routing.Library/Web.fs`. What changed:

- **`Use` with the context.** `Use(Func<HttpContext, RequestDelegate, Task>)` is the request handler itself;
  before, every `Use` was called with a next delegate to get the handler, which ran the middleware at startup.
- **User code is run when the pipeline is the application's own.** A request root that is a user function and
  not just a call into a library (`isForwarder`) is run like library code. The handler is chosen where the
  routing is decided: the user function whose branch last added conditions the route did not have yet (an
  active pattern's result counts when the user code tests it), or the first user function it calls after that,
  while it runs. A router that answers with a value itself (`| GET ["users"] -> Some response`) is its own
  handler (`HandlerRouter`), and its open segments are not named after its parameters. A path that calls the
  next part of the pipeline (`next`, a delegate nothing is known about) was not handled there and gives no
  entry. Outcomes that decided different routes stay apart, and the limits on outcomes and threads per call are
  higher in this mode (512 times the base for both), as each route is a path of its own to the end.
- **Path segments in F# lists.** The path split and turned into a list (`List.ofArray`, `List.filter`) stays the
  path's segments; head, tail and length of it are answered from the segment tags (the head of the segments from
  n is segment n), as request data tracking does, instead of running FSharp.Core's list code over them.
- **Other request collections.** In this mode an F# collection function given request data that is not the
  path (the headers or the query string folded into a map, a key looked up in it) gives request data without
  being run: its elements are unknown, and running it only forked until the budget was spent. A route that also
  needs a query key is reported without that condition.
- **Contradictions.** A path of n segments has no segment n or later: such a pair of conditions ends the path.
- **Open segments.** In a route of known length, a segment no constant was compared with (`Int32Str id`, `_`)
  is a parameter, `{param}`.
- Request data tracking finds the middleware's plumbing (the function that maps the request, the response
  writer) as handlers with no route; inside a pipeline whose routing was followed this way they are dropped.

On the application this came from, all 70 routes are recovered, complete, besides `OPTIONS` on any path; the
symbolic run takes 2 s. (The static part of its entry point analysis takes over two minutes, before and after.)

### A case per route, each calling its handler

The application the fixture was made from still lost nearly all its routes: its stand-in had replaced every
handler with one shared response. With a handler per case, called with what the case's patterns bind
(`| GET (["Roles"; Int32Str id], _) -> Some (Roles.show db id)`), F# no longer compiles the match to one
decision tree but to a chain: each case calls the method pattern again and tests the path, and a failed test
jumps to the next case. Given a handler per case, the stand-in lost all 68 routes under its two prefixes. Fixture:
`Routing.Wrapped`'s `Shop.fs`, ten resources with six cases each, none of which was found before. What changed:

- **Paths merge where they meet.** A failed test leaves no predicate, so a path that fails case k one of several
  ways (another method, too short, another first segment, too long) reaches case k + 1 once per way, and every
  copy forks again at every later case: the thread limit was reached about ten cases in. A thread is not queued
  again when one with the same observable state was queued at that instruction before. What is compared is what
  the rest of the method can see: the locals still read from there on (each case has its own locals for what its
  patterns bind, which later cases never read; `ControlFlow.liveLocals`), the heap objects the stack, locals,
  arguments and statics reach (a failed case leaves the option its pattern returned behind), the predicates as a
  set, and the rest of the path state.
- **The limits.** Paths that are different still add up: one that matched a case's first segment but not its
  length goes on through every later case. Their number grows with the square of the cases, so in this mode the
  limits on threads and outcomes per call are 512 times the base (32,768 threads, 2,048 outcomes). Each such path
  is rejected by a later case within a few instructions, and the step budget still bounds the run: the fixture's
  takes 450,000 steps.
- **Parsed segments.** `TryParse` of request data (`Int32.TryParse(segment, &id)`, what `Int32Str id` compiles
  to) writes that data to its out argument: the number handed to the handler is the segment it came from, a
  parameter named after the handler's parameter it is handed as (`{id}`). A segment the path already compared
  with a constant parses as that constant does, so `POST [x; Int32Str id]` after `POST [x; "New"]` does not
  take "New" too.
- **The other open segments.** A handler handed some segments also gets the other open ones of a route of known
  length as parameters (`{param}`, as a router that answers itself does); a handler handed none, such as a
  not-found one reached on a fall-through, still gets none. A route some of whose parameters are named after what
  they were handed as is not renamed after the handler's parameters in order.

A route whose only open segment the handler is not handed (`["RolesByType"; _] -> RolesByType.all db`) is
reported as partial, `/RolesByType/*`: that is also how a fall-through to a not-found handler looks.

### A router run by a library call

The owner's reproduction (2026-11-26) of the application's missing routes: the router is combined with a
fallback before it is handed to the middleware (`thenTry fallback routing = fun request -> routing request |>
Option.orElseWith (fun () -> fallback request)`), so the router runs inside FSharp.Core's `Option.orElseWith`.
A library call two or more calls deep runs within a budget of its own (150,000 steps), so that a library's own
loop cannot spend the whole run; when it runs out, the call yields an unknown value and its paths are dropped.
Routing 72 cases takes about 205,000 steps, so every route was lost and only the middleware was left. Now, in a
pipeline the application wrote, a library call handed one of the application's functions keeps its caller's
budget: what it runs is the application's routing. Fixture: `Routing.Wrapped`'s `Program.fs` hands the
middleware its router through such a `thenTry`; before, it lost all its routes. The fallback `fun _ -> None`,
handed the request and answering nothing, is not reported as a handler no request reaches: the run called it.

## Known limits

- Path explosion is bounded, not solved: frameworks that try alternatives one after another fork at every
  unknown value, and an application with many alternatives can still run out of budget before the last ones.
  The call depth limit is 96 (nested routers and recursive helpers need it); the interpreter runs on its own
  thread with a 512 MB stack so that depth cannot overflow the caller's. A call into a dependency gets at most
  150,000 steps, shared with the calls inside it, so an alternative tried after a large one can still be left
  without budget in a bigger application than the fixtures.
- Libraries' code that registers services is not run: the shared framework never is, nor is a package's copy of
  an assembly a shared framework also ships (dependency injection, options, configuration, logging, hosting),
  newer than the framework's, as OpenTelemetry's packages bring. Run, their
  loops over the service collection (`TryAddSingleton`) forked on every descriptor: 80 s for an application that
  sets up OpenTelemetry. A library's recursive call while the application registers, with the same arguments as
  the call it is in (OpenTelemetry's search for the Prometheus exporter through nested metric readers of unknown
  shape), yields an unknown value: it learns nothing new and forked at every level, 43 s more. Recursion over
  known values (FSharp.Core's over a list, which Saturn's routers are built with), user code's, and a router's
  while it handles a request are followed as before.
- An endpoint whose handler is a lambda C# caches in a static field (`<>9__0_4 ?? new Func<...>(...)`) is read
  from its registration, as one with any other handler: it was left to this run, and lost where the run ran out of
  budget first. Where the run finds a route for the handler, the run's stands: it follows a group's prefix from a
  caller and the endpoint filters, which the registration scan does not.
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
