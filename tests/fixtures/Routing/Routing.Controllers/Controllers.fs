namespace Routing.Controllers

open Microsoft.AspNetCore.Mvc

/// A class route with the [controller] token, and one action per HTTP method.
[<ApiController>]
[<Route("api/[controller]")>]
type OrdersController() =
    inherit ControllerBase()

    [<HttpGet>]
    member _.List() = "all orders"

    [<HttpGet("{id:int}")>]
    member _.Get(id: int) = $"order {id}"

    [<HttpPost>]
    member _.Create() = "created"

    [<HttpPut("{id:int}")>]
    member _.Replace(id: int) = $"replaced {id}"

    [<HttpDelete("{id:int}")>]
    member _.Remove(id: int) = $"deleted {id}"

    [<HttpPatch("{id:int}")>]
    member _.Patch(id: int) = $"patched {id}"

    [<HttpOptions>]
    member _.Options() = "GET, POST, PUT, DELETE, PATCH"

    /// Public, but not an action.
    [<NonAction>]
    member _.Describe() = "orders"

    /// A property: not an action.
    member _.Count = 3

/// Two verbs on one action, and a route that ignores the class route.
[<Route("health")>]
type HealthController() =
    inherit ControllerBase()

    [<HttpGet>]
    [<HttpHead>]
    member _.Check() = "ok"

    [<AcceptVerbs("GET", "POST", Route = "/ping")>]
    member _.Ping() = "pong"

/// Excluded from MVC.
[<NonController>]
[<Route("legacy")>]
type LegacyController() =
    inherit ControllerBase()

    [<HttpGet>]
    member _.Old() = "old"

/// Not a controller: no Controller suffix and no [Controller].
type OrderFormatter() =
    member _.Format(id: int) = $"#{id}"
