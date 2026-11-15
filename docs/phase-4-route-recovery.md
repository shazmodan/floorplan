# Phase 4: route recovery, status

Date: 2026-11-15. Results: `tests/golden/routing.entry-points.txt`.

| Fixture application | Form | Result |
|---|---|---|
| Routing.Endpoints (MapGet, MapGroup, MapMethods) | C | All routes complete |
| Routing.Middleware (`&&` conditions, `StartsWith`, `Split` and index) | A | `GET /health`, `GET /orders/{param}` complete; `POST /orders/*` partial; the catch-all unresolved |
| Routing.Listener (list patterns, active patterns) | A | `GET /api/orders`, `POST /api/orders`, `GET /api/orders/{param}` complete |
| Routing.Table (Dictionary and Map of handlers) | B | Paths recovered; partial, because nothing constrains the method |
| Routing.Dynamic (route from configuration, regex built at run time) | negative | Handlers found, no route: never a wrong one |
| Routing.Socket (raw TCP) | none | Network entry point, unresolved |

Operations recognized (plan section 6.4, form A): equality with a constant (`=`, `String.Equals`, string
patterns), `StartsWith`/`EndsWith`/`Contains`, `PathString.StartsWithSegments`, constant `Regex.IsMatch`,
`Split` followed by indexing or list patterns, segment counts (`Length`, list end), helpers that return bool or
an option (active patterns) through a "succeeds when" summary, and boolean values merged by `&&`/`||`.
Conditions in callers are collected up to the root, one route per call path.

## Known limits

- Routing code inside a dependency (a NuGet package), including function-combinator routers, is followed by the
  symbolic interpreter added later; see `docs/framework-routing.md`.
- Form B tables are recognized when the table lives in a field or module value and is built with constant keys in
  the method that stores it.
