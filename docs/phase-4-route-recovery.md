# Phase 4: route recovery, status

Date: 2026-11-15. Results: `tests/golden/routing.entry-points.txt`.

| Fixture application | Form | Result |
|---|---|---|
| Routing.Endpoints (MapGet, MapGroup, MapMethods, nested groups, groups passed to functions, lambdas, `RequireAuthorization`/`WithName` chains, a route built at run time) | C | All 12 constant routes complete; the run-time route partial (method only) |
| Routing.Middleware (`&&` conditions, `StartsWith`, `Split` and index) | A | `GET /health`, `GET /orders/{id}` complete; `POST /orders/*` partial; the catch-all unresolved |
| Routing.Listener (list patterns, active patterns) | A | `GET /api/orders`, `POST /api/orders`, `GET /api/orders/{id}` complete |
| Routing.Records (active patterns made by partially applying an inline matcher, path in a nested record, path and query as a tuple) | A | `GET /Session`, `POST /Session`, `DELETE /Session/{id}` complete (GitHub issue 17) |
| Routing.Table (Dictionary and Map of handlers) | B | Paths recovered; partial, because nothing constrains the method |
| Routing.Delegated (the Middleware router in a library outside the solution, handlers passed in a record) | A, in a dependency | Same routes as Routing.Middleware, by the symbolic interpreter |
| Routing.Wrapped (its own `app.Use((context, next) => ...)` middleware calling the routing function it is handed; a package's active patterns, or-patterns, prefixes taken off, routes answering with a shared value) | A, through a function value | All 11 routes, by the symbolic interpreter (`docs/framework-routing.md`, 2026-11-23) |
| Routing.Dynamic (route from configuration, regex built at run time) | negative | Handlers found, no route: never a wrong one |
| Routing.Socket (raw TCP) | none | Network entry point, unresolved |

Operations recognized (plan section 6.4, form A): equality with a constant (`=`, `String.Equals`, string
patterns), `StartsWith`/`EndsWith`/`Contains`, `PathString.StartsWithSegments`, constant `Regex.IsMatch`,
`Split` followed by indexing or list patterns, segment counts (`Length`, list end), helpers that return bool or
an option (active patterns) through a "succeeds when" summary, and boolean values merged by `&&`/`||`. An active
pattern that is a function value (`let (|GET|_|) = matchRequest "GET"`) is a closure held in a static field: its
`Invoke` is summarized the same way, with the constants the closure captured standing for its fields.
Conditions in callers are collected up to the root, one route per call path. A path segment taken as a parameter
is named after the handler's parameter that receives it (`{id}` for `Orders.show ctx id`) when the handler has
as many such parameters, other than the request pipeline's own, as the route has; otherwise it stays `{param}`.
A branch on request data in code that a handler with a route runs (the handler, and what it calls) is the
handler's own logic, such as a check of the content type, not routing: the functions it calls are not reported as
handlers (`Routing.Endpoints/Verify.fs`, GitHub issue 1).

## Known limits

- Minimal APIs: a group received as a parameter (`RouteGroupBuilder` or `IEndpointRouteBuilder`) has a prefix
  only its callers know. The static scan reports such routes without a route; the symbolic interpreter, which runs
  from `main`, follows the call and gives the full route. In a project with no entry point those routes stay
  partial. A lambda handler keeps its compiler-generated name in the JSON model (for example
  `Admin/Pipe #1 input at line 24@24::Invoke`, which also keys its traces); the site shows it as written:
  `Admin › lambda at line 24`, or `Handlers.price › async body at line 14` for a task body.
- Routing code inside a dependency (a NuGet package), including function-combinator routers, is followed by the
  symbolic interpreter added later; see `docs/framework-routing.md`.
- Form B tables are recognized when the table lives in a field or module value and is built with constant keys in
  the method that stores it.
