# MVC controllers: status

Date: 2026-11-16. Fixtures: `tests/fixtures/Routing/Routing.Controllers` (attribute routing) and
`tests/fixtures/Routing/Routing.Conventional` (conventional routes, areas, inherited routes). Results:
`tests/golden/routing.entry-points.txt`.

An application that maps controllers gets one entry point per controller action and route, read from the
declarations and from the route patterns it maps, not from calls. All platform facts are catalog rules
(`net8.0.json`, inherited by later catalogs): the registrations (`MapControllers`, `MapControllerRoute`,
`MapAreaControllerRoute`, and `MapDefaultControllerRoute` with the platform's default pattern
`{controller=Home}/{action=Index}/{id?}`), and the MVC attribute types with what their constructor arguments mean
(`[Route]` and `[HttpGet]`... give a template, `[HttpGet]`... a method, `[AcceptVerbs]` a list of methods,
`[Area]` an area, `[Controller]`, `[NonController]` and `[NonAction]` mark what is and is not a controller or
action).

How MVC's rules are followed:

- **Controllers** are public, concrete, non-generic classes in the application's assembly or the user assemblies it
  references, named `...Controller` or carrying `[Controller]` (inherited: `ControllerBase` carries it), and
  without `[NonController]`.
- **Actions** are their public instance methods, including those inherited from base classes in user code,
  except constructors, property accessors and `[NonAction]` methods. `[ActionName]` renames an action for
  routing and for the `[action]` token.
- **Attribute routes** (with `MapControllers`) combine the controller's `[Route]`, its own or inherited from a
  base controller, with the action's template; an action template starting with `/` or `~/` stands alone.
  `[controller]`, `[action]` and `[area]` are replaced by the controller name (without `Controller`), the method
  name and the controller's `[Area]`. Each verb attribute gives its own entry, so an action with `[HttpGet]` and
  `[HttpHead]` has two; an action with no verb attribute matches any method and is reported without one
  (partial).
- **Conventional routes** reach the actions that have no attribute route. Each pattern the application maps
  gives an action the pattern with `{controller}`, `{action}` and `{area}` filled in, plus the shorter forms its
  defaults allow: `{controller=Home}/{action=Index}/{id?}` gives `Home.Index` the routes `/Home/Index/{id?}`,
  `/Home` and `/`. A controller with an `[Area]` is reached only by a pattern with an `{area}` parameter or by the
  area route of its area; one without is reached only by patterns without either. A verb attribute without a
  template still restricts the method; without one, the action matches any method (partial).

On the fixtures: 11 of 11 attribute routes complete in `Routing.Controllers`; in `Routing.Conventional`, the
default route with its shorter forms, an area route, a POST-only conventional action, a `[Route]` inherited from
an abstract base controller and an `[area]` token.

Known limits:

- A conventional pattern that names no `{controller}` and `{action}` parameters, and takes them from a defaults
  object instead (`MapControllerRoute("blog", "blog/{*article}", new { controller = "Blog", action = "Article" })`),
  reaches nothing the analysis can name: those actions keep no route. Defaults and constraints objects are not
  read, and neither is a pattern that is not a constant.
- Route prefixes from application model conventions (`IApplicationModelConvention`), and controllers registered
  some other way (an `IApplicationFeatureProvider`, controllers from assemblies outside the solution) are not
  followed.
