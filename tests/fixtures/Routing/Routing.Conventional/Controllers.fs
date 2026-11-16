namespace Routing.Conventional

open Microsoft.AspNetCore.Mvc

/// No routing attributes: reached by the default conventional route, {controller=Home}/{action=Index}/{id?}.
type HomeController() =
    inherit ControllerBase()

    member _.Index() = "home"

    member _.About() = "about"

    /// A method attribute without a template still restricts a conventionally routed action.
    [<HttpPost>]
    member _.Contact() = "sent"

type ProductsController() =
    inherit ControllerBase()

    member _.Index() = "products"

    member _.Details(id: int) = $"product {id}"

    /// Routed under the name [ActionName] gives it, not the method's.
    [<ActionName("Show")>]
    member _.Display(id: int) = $"showing {id}"

/// In an area: reached by the area route, not by the default route.
[<Area("Admin")>]
type UsersController() =
    inherit ControllerBase()

    member _.Index() = "users"

    member _.Edit(id: int) = $"user {id}"

/// A route on a base controller applies to the controllers derived from it, and so do its actions.
[<AbstractClass>]
[<Route("api/[controller]")>]
[<ApiController>]
type ApiControllerBase() =
    inherit ControllerBase()

    [<HttpGet("ping")>]
    member _.Ping() = "pong"

type InvoicesController() =
    inherit ApiControllerBase()

    [<HttpGet>]
    member _.List() = "invoices"

    [<HttpGet("{id}")>]
    member _.Get(id: int) = $"invoice {id}"

/// The [area] token of an attribute route.
[<Area("Reports")>]
[<Route("[area]/[controller]")>]
type SalesController() =
    inherit ControllerBase()

    [<HttpGet>]
    member _.Summary() = "sales"
