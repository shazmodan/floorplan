# MVC controllers: status

Date: 2026-11-16. Fixtures: `tests/fixtures/Routing/Routing.Controllers` (attribute routing) and
`tests/fixtures/Routing/Routing.Conventional` (conventional routes, defaults objects, areas, inherited routes),
`Routing.Conventions` and `Routing.Prefixed` (application model conventions). Results:
`tests/golden/routing.entry-points.txt`.

An application that maps controllers gets one entry point per controller action and route, read from the
declarations and from the route patterns it maps, not from calls. All platform facts are catalog rules
(`net8.0.json`, inherited by later catalogs): the registrations (`MapControllers`, `MapControllerRoute` and
`MapAreaControllerRoute` with their pattern and defaults arguments, and `MapDefaultControllerRoute` with the
platform's default pattern `{controller=Home}/{action=Index}/{id?}`), the application model convention interfaces
and the model members through which a convention changes routes, and the MVC attribute types with what their constructor arguments mean
(`[Route]` and `[HttpGet]`... give a template, `[HttpGet]`... a method, `[AcceptVerbs]` a list of methods,
`[Area]` an area, `[Controller]`, `[NonController]` and `[NonAction]` mark what is and is not a controller or
action).

How MVC's rules are followed:

- **Controllers** are public, concrete, non-generic classes in the application's assembly or the user assemblies it
  references, named `...Controller` or carrying `[Controller]` (inherited: `ControllerBase` carries it), and
  without `[NonController]`.
- **Actions** are their public instance methods, including those inherited from base classes in user code,
  except constructors, property accessors and `[NonAction]` methods. As MVC reads `[NonAction]` with
  inheritance, an override of a `[NonAction]` method is not an action either (`OnActionExecuting` and the other
  filter methods of `Controller`), nor is an override of an `object` method, or `Dispose()`. `[ActionName]` renames an action for
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
- **Defaults objects** (`new { controller = "Blog", action = "Article" }`, or an F# anonymous record) are read
  for their `controller`, `action` and `area` values when those are constants. They act as defaults of the
  pattern's parameters, and where the pattern has no such parameter they name the only controller, action or area
  the route reaches: `MapControllerRoute("blog", "blog/{*article}", new { controller = "Blog", action = "Article" })`
  gives `Blog.Article` the route `/blog/{*article}` and no other action anything.
- **Application model conventions** (`IApplicationModelConvention`, `IControllerModelConvention`,
  `IActionModelConvention`) are not evaluated; their effect is bounded. A convention the application creates (to
  add to `MvcOptions.Conventions`) applies to every action, and a convention attribute to the controller or action
  it is on. When its `Apply`, through user code and the closures it creates, uses a model member that changes
  routes (a selector's `AttributeRouteModel`, a new `SelectorModel` or `AttributeRouteModel`, `ControllerName`,
  `ActionName`, `RouteValues`, action constraints or endpoint metadata), the routes it applies to are partial, with
  a step that names the convention and the member. A platform convention, or one whose code cannot be read, counts
  as changing routes. A convention that touches none of those (adding filters or properties) changes nothing.

On the fixtures: 11 of 11 attribute routes complete in `Routing.Controllers`; in `Routing.Conventional`, the
default route with its shorter forms, an area route, a route whose controller and action come from its defaults
object, a POST-only conventional action, a `[Route]` inherited from an abstract base controller and an `[area]`
token; in `Routing.Conventions`, a convention that only marks controllers leaves a route complete while a
controller attribute and an action attribute that rewrite routes leave theirs partial; in `Routing.Prefixed`, an
application-wide route prefix convention leaves every route partial.

Known limits:

- Patterns that are not constants are not read, and neither are defaults objects of another kind (a
  `RouteValueDictionary`, a variable) or with values that are not constants: a pattern that names the controller
  and action then gets only its full form, and one that does not gives every conventionally routed action an
  unresolved entry. Constraints objects are not read.
- What a convention does to a route is not computed: a route prefix convention leaves `/items` partial, not
  `/api/items`. A convention that only removes controllers or actions from the model's collections is not seen,
  nor one created in a way the analysis does not see (by reflection, or in a dependency outside the solution).
- Controllers registered some other way (an `IApplicationFeatureProvider`, controllers from assemblies outside
  the solution) are not followed.
