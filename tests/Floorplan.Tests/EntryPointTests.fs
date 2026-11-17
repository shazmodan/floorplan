module Floorplan.Tests.EntryPointTests

open System.Text
open Expecto
open Floorplan.Model
open Floorplan.Cli
open Floorplan.Analysis.EntryPoints

let routingModel = lazy (Pipeline.buildModel ignore Floorplan.Model.Caching.Store.Disabled [] [] (Fixtures.routing.Force()))

let routingModelNet10 =
    lazy (Pipeline.buildModel ignore Floorplan.Model.Caching.Store.Disabled [] [] (Fixtures.routingNet10.Force()))

let render (model: AnalysisModel) =
    let b = StringBuilder()

    for e in model.EntryPoints do
        let predicates =
            e.Predicates
            |> List.map (fun p ->
                let index = match p.Index with Some i -> string i | None -> ""
                $"{Json.unionName p.Part}{index} {Json.unionName p.Operator} {p.Value}")
            |> String.concat "; "

        let route = defaultArg e.Route "-"
        let httpMethod = defaultArg e.HttpMethod "-"
        let forms = e.Forms |> List.map Json.unionName |> String.concat ","

        // A lambda's class is named by the compiler, and compilers name it differently: keep only its module.
        let handlerMember =
            let fsharp = System.Text.RegularExpressions.Regex.Replace(e.HandlerMember, @"/[^/:|]*@[^/:|]*::Invoke$", "/<lambda>")
            System.Text.RegularExpressions.Regex.Replace(fsharp, @"/<>c(__DisplayClass[\d_]+)?::<.*>b__[\d_]+$", "/<lambda>")

        b.AppendLine(
            $"{e.ApplicationId} | {Json.unionName e.Category} | {httpMethod} {route} | {Json.unionName e.Completeness} | {e.HandlerUnit} | {handlerMember} | forms [{forms}] | [{predicates}]"
        )
        |> ignore

    b.ToString()

let private entries (model: AnalysisModel) (app: string) =
    model.EntryPoints |> List.filter (fun e -> e.ApplicationId = app)

let private routes (model: AnalysisModel) (app: string) =
    entries model app
    |> List.map (fun e -> defaultArg e.HttpMethod "-", defaultArg e.Route "-", e.HandlerMember, e.Completeness)
    |> List.sort

let private handlers (model: AnalysisModel) (app: string) =
    entries model app |> List.map (fun e -> e.HandlerMember) |> List.sort

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Entry points"
        [ test "golden: Routing fixture" { Golden.check "routing.entry-points.txt" (render (routingModel.Force())) }

          test "golden: Routing fixture on net10.0 gives the net8.0 results" {
              let m = routingModelNet10.Force()
              Expect.isTrue (m.Projects |> List.forall (fun p -> p.TargetFramework = "net10.0")) "every project on net10.0"
              Golden.check "routing.entry-points.txt" (render m)
          }

          test "golden: Shop fixture" { Golden.check "shop.entry-points.txt" (render (ExitPointTests.shopModel.Force())) }

          test "MVC controllers: conventional routes, areas, inherited [Route] and the [area] token" {
              let m = routingModel.Force()

              let routes =
                  entries m "Routing.Conventional"
                  |> List.map (fun e ->
                      defaultArg e.HttpMethod "*",
                      defaultArg e.Route "-",
                      e.HandlerMember.Substring(e.HandlerMember.LastIndexOf '.' + 1),
                      e.Completeness)
                  |> List.sort

              Expect.equal
                  routes
                  [ // Any method: conventional routes do not restrict it, so these are partial.
                    "*", "/", "HomeController::Index", Completeness.Partial
                    "*", "/Admin/Users", "UsersController::Index", Completeness.Partial
                    "*", "/Admin/Users/Edit/{id?}", "UsersController::Edit", Completeness.Partial
                    "*", "/Admin/Users/Index/{id?}", "UsersController::Index", Completeness.Partial
                    "*", "/Blog/Article/{id?}", "BlogController::Article", Completeness.Partial
                    "*", "/Home", "HomeController::Index", Completeness.Partial
                    "*", "/Home/About/{id?}", "HomeController::About", Completeness.Partial
                    "*", "/Home/Index/{id?}", "HomeController::Index", Completeness.Partial
                    "*", "/Products", "ProductsController::Index", Completeness.Partial
                    "*", "/Products/Details/{id?}", "ProductsController::Details", Completeness.Partial
                    "*", "/Products/Index/{id?}", "ProductsController::Index", Completeness.Partial
                    "*", "/Products/Show/{id?}", "ProductsController::Display", Completeness.Partial
                    // The blog route: its defaults object names the controller and action.
                    "*", "/blog/{*article}", "BlogController::Article", Completeness.Partial
                    // Attribute routes: [Route] on a base controller, and [area] filled from [Area].
                    "GET", "/Reports/Sales", "SalesController::Summary", Completeness.Complete
                    "GET", "/api/Invoices", "InvoicesController::List", Completeness.Complete
                    // An action declared on the base controller, routed for the derived one.
                    "GET", "/api/Invoices/ping", "ApiControllerBase::Ping", Completeness.Complete
                    "GET", "/api/Invoices/{id}", "InvoicesController::Get", Completeness.Complete
                    // [HttpPost] without a template restricts the conventionally routed action.
                    "POST", "/Home/Contact/{id?}", "HomeController::Contact", Completeness.Complete ]
                  "routes"

              let combined =
                  conventionalRoutes "{controller=Home}/{action=Index}/{id?}" None Map.empty "Home" "Index" None

              Expect.equal combined [ "/Home/Index/{id?}"; "/Home"; "/" ] "defaults give the shorter forms"
              Expect.isEmpty (conventionalRoutes "{controller=Home}/{action=Index}/{id?}" None Map.empty "Users" "Index" (Some "Admin")) "an area controller is not reached by a route without an area"
              Expect.equal (conventionalRoutes "{area:exists}/{controller=Home}/{action=Index}" None Map.empty "Users" "Index" (Some "Admin")) [ "/Admin/Users/Index"; "/Admin/Users" ] "{area} filled in"
              Expect.isEmpty (conventionalRoutes "blog/{*article}" None Map.empty "Blog" "Article" None) "a pattern without controller and action, and no defaults, reaches nothing it can name"
              let blog = Map.ofList [ "controller", "Blog"; "action", "Article" ]
              Expect.equal (conventionalRoutes "blog/{*article}" None blog "Blog" "Article" None) [ "/blog/{*article}" ] "the defaults object names the action"
              Expect.isEmpty (conventionalRoutes "blog/{*article}" None blog "Blog" "Index" None) "only the action the defaults name"
              Expect.isEmpty (conventionalRoutes "blog/{*article}" None blog "Home" "Article" None) "only the controller the defaults name"
              let home = Map.ofList [ "controller", "Home"; "action", "Index" ]
              Expect.equal (conventionalRoutes "{controller}/{action}/{id?}" None home "Home" "Index" None) [ "/Home/Index/{id?}"; "/Home"; "/" ] "defaults object values act as parameter defaults"
              Expect.equal (conventionalRoutes "docs/{action}" None (Map.ofList [ "controller", "Docs"; "area", "Help" ]) "Docs" "Read" (Some "Help")) [ "/docs/Read" ] "an area default requires that area"
              Expect.isEmpty (conventionalRoutes "docs/{action}" None (Map.ofList [ "controller", "Docs"; "area", "Help" ]) "Docs" "Read" None) "a controller outside the area is not reached"
          }

          test "MVC controllers: attribute routes with [controller], verbs, AcceptVerbs, NonAction and NonController" {
              let c = "Routing.Controllers."

              Expect.equal
                  (routes (routingModel.Force()) "Routing.Controllers")
                  [ "DELETE", "/api/Orders/{id:int}", c + "OrdersController::Remove", Completeness.Complete
                    "GET", "/api/Orders", c + "OrdersController::List", Completeness.Complete
                    "GET", "/api/Orders/{id:int}", c + "OrdersController::Get", Completeness.Complete
                    "GET", "/health", c + "HealthController::Check", Completeness.Complete
                    "GET", "/ping", c + "HealthController::Ping", Completeness.Complete
                    "HEAD", "/health", c + "HealthController::Check", Completeness.Complete
                    "OPTIONS", "/api/Orders", c + "OrdersController::Options", Completeness.Complete
                    "PATCH", "/api/Orders/{id:int}", c + "OrdersController::Patch", Completeness.Complete
                    "POST", "/api/Orders", c + "OrdersController::Create", Completeness.Complete
                    "POST", "/ping", c + "HealthController::Ping", Completeness.Complete
                    "PUT", "/api/Orders/{id:int}", c + "OrdersController::Replace", Completeness.Complete ]
                  "every action once per method, nothing from [NonAction], properties, [NonController] or plain classes"
          }

          test "MVC controllers: application model conventions that may change routes leave them partial" {
              let m = routingModel.Force()
              let c = "Routing.Conventions."

              Expect.equal
                  (routes m "Routing.Conventions")
                  [ // No convention touches it: the application-wide AuditConvention only marks controllers.
                    "GET", "/api/Customers", c + "CustomersController::List", Completeness.Complete
                    // [LegacyName] renames the action, so [action] may not be "Find".
                    "GET", "/api/Customers/Find", c + "CustomersController::Find", Completeness.Partial
                    // [Versioned] replaces the controller's routes.
                    "GET", "/api/Reports", c + "ReportsController::Summary", Completeness.Partial ]
                  "only the routes a convention may change are partial"

              let steps (handler: string) =
                  entries m "Routing.Conventions"
                  |> List.filter (fun e -> e.HandlerMember = c + handler)
                  |> List.collect (fun e -> e.Evidence.Steps |> List.map (fun s -> s.Description))

              Expect.exists (steps "ReportsController::Summary") (fun d -> d.Contains "VersionedAttribute" && d.Contains "AttributeRouteModel") "the controller attribute and how it changes routes"
              Expect.exists (steps "CustomersController::Find") (fun d -> d.Contains "LegacyNameAttribute" && d.Contains "set_ActionName") "the action attribute and how it changes routes"
              Expect.isFalse (entries m "Routing.Conventions" |> List.exists (fun e -> e.Evidence.Steps |> List.exists (fun s -> s.Description.Contains "AuditConvention"))) "a convention that changes no routes is not reported"

              Expect.equal
                  (routes m "Routing.Prefixed")
                  [ "GET", "/items", "Routing.Prefixed.ItemsController::List", Completeness.Partial ]
                  "an application-wide prefix convention leaves every route partial"

              Expect.exists
                  (entries m "Routing.Prefixed" |> List.collect (fun e -> e.Evidence.Steps |> List.map (fun s -> s.Description)))
                  (fun d -> d.Contains "creates the application model convention Routing.Prefixed.RoutePrefixConvention")
                  "where the application creates the convention"
          }

          test "controller routes combine as MVC combines them" {
              Expect.equal (combineRoutes (Some "api/orders") (Some "{id}")) (Some "/api/orders/{id}") "relative"
              Expect.equal (combineRoutes (Some "api/orders") (Some "/ping")) (Some "/ping") "absolute"
              Expect.equal (combineRoutes (Some "api/orders") (Some "~/ping")) (Some "/ping") "application-relative"
              Expect.equal (combineRoutes (Some "api/orders") None) (Some "/api/orders") "class route only"
              Expect.equal (combineRoutes None None) None "not attribute routed"
          }

          test "endpoint routing gives complete routes, including groups and method lists" {
              let m = routingModel.Force()

              let routes =
                  entries m "Routing.Endpoints"
                  |> List.map (fun e -> defaultArg e.HttpMethod "", defaultArg e.Route "", e.Completeness)
                  |> List.sort

              Expect.equal
                  routes
                  [ "DELETE", "/api/orders/{id:int}", Completeness.Complete
                    // A route template built at run time: the method is known, the route is not.
                    "GET", "", Completeness.Partial
                    "GET", "/admin/reports/daily", Completeness.Complete
                    "GET", "/admin/users", Completeness.Complete
                    "GET", "/admin/users/{id:int}", Completeness.Complete
                    "GET", "/api/orders", Completeness.Complete
                    "GET", "/api/orders/{id:int}", Completeness.Complete
                    "GET,HEAD", "/health", Completeness.Complete
                    "OPTIONS", "/api/orders", Completeness.Complete
                    "PATCH", "/api/orders/{id:int}", Completeness.Complete
                    "POST", "/admin/reports/rebuild", Completeness.Complete
                    "POST", "/v2/orders", Completeness.Complete
                    "PUT", "/api/orders/{id:int}", Completeness.Complete ]
                  "routes"

              // A group handed to a function gets its caller's prefix, and only that: no unprefixed /users.
              let users = entries m "Routing.Endpoints" |> List.filter (fun e -> e.HandlerMember.EndsWith "::listUsers")
              Expect.equal (users |> List.map (fun e -> e.Route)) [ Some "/admin/users" ] "group from a caller"

              // A lambda is its own handler, not a library response, behind convention calls such as
              // RequireAuthorization.
              let rebuild = entries m "Routing.Endpoints" |> List.find (fun e -> e.Route = Some "/admin/reports/rebuild")
              Expect.stringStarts rebuild.HandlerMember "Routing.Endpoints.Admin/" "lambda handler"
              Expect.isFalse (rebuild.HandlerMember.Contains "library function") "not a library response"

              let getOrder = entries m "Routing.Endpoints" |> List.find (fun e -> e.HandlerMember.EndsWith "getOrder")
              Expect.equal getOrder.HandlerUnit "Routing.Endpoints:Routing.Endpoints.Api" "handler module"
              Expect.equal getOrder.Forms [ RouteForm.PlatformRouting ] "form C"

              Expect.exists
                  getOrder.Predicates
                  (fun p -> p.Part = RequestPart.PathSegment && p.Operator = PredicateOperator.Parameter && p.Index = Some 2)
                  "route parameter"
          }

          test "form A in a dependency: the Middleware router moved into a library gives the same routes" {
              let m = routingModel.Force()

              // Handler routes by handler name, with parameter names left out: the symbolic run names them.
              let routes app =
                  entries m app
                  |> List.filter (fun e -> not (e.HandlerMember.EndsWith "NotFound::handle"))
                  |> List.map (fun e ->
                      e.HandlerMember.Substring(e.HandlerMember.LastIndexOf '.' + 1),
                      defaultArg e.HttpMethod "-",
                      System.Text.RegularExpressions.Regex.Replace(defaultArg e.Route "-", @"\{[^}]*\}", "{param}"),
                      e.Completeness)
                  |> List.sort

              Expect.isNonEmpty (routes "Routing.Delegated") "routes found through the dependency"
              Expect.equal (routes "Routing.Delegated") (routes "Routing.Middleware") "same routes as in user code"

              let projects = m.Projects |> List.map (fun p -> p.Id)
              Expect.isFalse (List.contains "Routing.Library" projects) "the library is a dependency, not a project"
          }

          test "form A, control flow: conditions combined across && and helper calls" {
              let m = routingModel.Force()

              Expect.equal
                  (routes m "Routing.Middleware")
                  [ "-", "-", "Routing.Middleware.NotFound::handle", Completeness.Unresolved
                    "-", "/orders/*", "Routing.Middleware.NotFound::handle", Completeness.Partial
                    "DELETE", "/orders/{id}", "Routing.Middleware.Orders::remove", Completeness.Complete
                    "GET", "/health", "Routing.Middleware.Health::check", Completeness.Complete
                    "GET", "/orders/{id}", "Routing.Middleware.Orders::show", Completeness.Complete
                    "OPTIONS", "/orders", "Routing.Middleware.Orders::options", Completeness.Complete
                    "PATCH", "/orders/{id}", "Routing.Middleware.Orders::patch", Completeness.Complete
                    "POST", "/orders/*", "Routing.Middleware.Orders::create", Completeness.Partial
                    "PUT", "/orders/{id}", "Routing.Middleware.Orders::replace", Completeness.Complete ]
                  "routes"

              let create = entries m "Routing.Middleware" |> List.find (fun e -> e.HandlerMember.EndsWith "create")
              Expect.equal create.Forms [ RouteForm.ControlFlow ] "form A"
          }

          test "form A, list patterns and active patterns: the illustration of plan section 6.5" {
              let m = routingModel.Force()

              Expect.equal
                  (routes m "Routing.Listener")
                  [ "DELETE", "/api/orders/{id}", "Routing.Listener.Orders::remove", Completeness.Complete
                    "GET", "/api/orders", "Routing.Listener.Orders::list", Completeness.Complete
                    "GET", "/api/orders/{id}", "Routing.Listener.Orders::fetch", Completeness.Complete
                    "OPTIONS", "/api/orders", "Routing.Listener.Orders::options", Completeness.Complete
                    "PATCH", "/api/orders/{id}", "Routing.Listener.Orders::patch", Completeness.Complete
                    "POST", "/api/orders", "Routing.Listener.Orders::create", Completeness.Complete
                    "PUT", "/api/orders/{id}", "Routing.Listener.Orders::replace", Completeness.Complete ]
                  "routes"
          }

          test "form B, table lookup: dictionary and map entries" {
              let m = routingModel.Force()

              Expect.equal
                  (routes m "Routing.Table")
                  [ "-", "/health", "Routing.Table.Handlers::health", Completeness.Partial
                    "-", "/orders", "Routing.Table.Handlers::orders", Completeness.Partial
                    "-", "/users", "Routing.Table.Handlers::users", Completeness.Partial ]
                  "routes"

              Expect.isTrue (entries m "Routing.Table" |> List.forall (fun e -> e.Forms = [ RouteForm.TableLookup ])) "form B"
          }

          test "negative cases: routes from configuration and run-time regexes get no route" {
              let m = routingModel.Force()
              let dynamic = entries m "Routing.Dynamic"
              Expect.equal (handlers m "Routing.Dynamic") [ "Routing.Dynamic.Admin::run"; "Routing.Dynamic.Reports::run" ] "handlers found"

              for e in dynamic do
                  Expect.equal e.Completeness Completeness.Unresolved $"{e.HandlerMember} unresolved"
                  Expect.isNone e.Route $"{e.HandlerMember} has no route"
          }

          test "the site shows lambda handlers as written, not by their compiler-generated class" {
              match Browser.find () with
              | None -> skiptest "No Chromium-based browser found; set FLOORPLAN_BROWSER to run browser smoke tests."
              | Some browser ->
                  let dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "floorplan-tests", System.Guid.NewGuid().ToString "N")
                  let index = Floorplan.Rendering.WebsiteRenderer.render (routingModel.Force()) { Path = dir; SingleFile = false }
                  let page = Browser.dumpDom browser index "#/entry-points"

                  // Routing.Endpoints.Admin registers `fun () -> "daily"` on line 24 of Admin.fs.
                  Expect.stringContains page "Admin › lambda at line 24" "lambda by module and line"
                  Expect.isFalse (page.Contains ">Admin/Pipe #1") "no compiler-generated class name shown"
                  Expect.stringContains page "Api.getOrder" "named functions unchanged"
          }

          test "a raw socket gives an unresolved network entry point" {
              let m = routingModel.Force()

              match entries m "Routing.Socket" with
              | [ e ] ->
                  Expect.equal e.Category EffectCategory.Network "network"
                  Expect.equal e.Completeness Completeness.Unresolved "unresolved"
                  Expect.equal e.HandlerMember "Routing.Socket.Connection::handle" "handler"
              | other -> failtestf "expected one entry point, got %d" other.Length
          }

          test "Shop: minimal API handlers are found across projects" {
              let m = ExitPointTests.shopModel.Force()
              Expect.equal (handlers m "Shop.Web") [ "Shop.Web.Handlers::createOrder"; "Shop.Web.Handlers::listOrders"; "Shop.Web.Handlers::price" ] "handlers"
              Expect.isEmpty (entries m "Shop.Worker") "a worker has no entry points"
          } ]
