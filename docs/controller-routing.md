# MVC controllers: status

Date: 2026-11-15. Fixture: `tests/fixtures/Routing/Routing.Controllers`. Results: `tests/golden/routing.entry-points.txt`.

An application that calls `MapControllers` gets one entry point per controller action and route, read from the
declarations rather than from calls. All platform facts are catalog rules (`net8.0.json`, inherited by later
catalogs): the `MapControllers` registration, and the MVC attribute types with what their constructor arguments
mean (`[Route]` and `[HttpGet]`... give a template, `[HttpGet]`... a method, `[AcceptVerbs]` a list of methods,
`[Controller]`, `[NonController]` and `[NonAction]` mark what is and is not a controller or action).

How MVC's rules are followed:

- **Controllers** are public, concrete, non-generic classes in the application's assembly or the user assemblies it
  references, named `...Controller` or carrying `[Controller]` (inherited: `ControllerBase` carries it), and
  without `[NonController]`.
- **Actions** are their public instance methods, except constructors, property accessors and `[NonAction]`
  methods.
- **Routes** combine the controller's `[Route]` with the action's template; an action template starting with `/`
  or `~/` stands alone. `[controller]` and `[action]` are replaced by the controller name (without `Controller`)
  and the method name. Each verb attribute gives its own entry, so an action with `[HttpGet]` and `[HttpHead]` has
  two; an action with no verb attribute matches any method and is reported without one (partial).

On the fixture: 11 of 11 routes complete, including `[AcceptVerbs("GET", "POST", Route = "/ping")]`, and nothing
from `[NonAction]`, properties, a `[NonController]` class or a plain class.

Known limits:

- Conventional routing (`MapControllerRoute`, `MapDefaultControllerRoute`) is not followed: actions of controllers
  without attribute routes are reported without a route.
- Areas (`[area]`), route prefixes from application model conventions, and `[Route]` on base controller classes
  are not read.
- Controllers registered some other way (an `IApplicationFeatureProvider`, controllers from assemblies outside the
  solution) are not found.
