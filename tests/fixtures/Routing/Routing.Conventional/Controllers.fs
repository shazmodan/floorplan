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

/// In an area: reached by the area route, not by the default route.
[<Area("Admin")>]
type UsersController() =
    inherit ControllerBase()

    member _.Index() = "users"

    member _.Edit(id: int) = $"user {id}"

/// A route on a base controller applies to the controllers derived from it.
[<Route("api/[controller]")>]
[<ApiController>]
type ApiControllerBase() =
    inherit ControllerBase()

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
