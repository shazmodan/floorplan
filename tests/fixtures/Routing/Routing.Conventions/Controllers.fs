namespace Routing.Conventions

open Microsoft.AspNetCore.Mvc

/// No convention changes these routes: complete.
[<Route("api/[controller]")>]
type CustomersController() =
    inherit ControllerBase()

    [<HttpGet>]
    member _.List() = "customers"

    /// Renamed by a convention: [action] may not be "Find".
    [<HttpGet("[action]")>]
    [<LegacyName>]
    member _.Find() = "found"

/// Its routes are rewritten by the convention attribute: partial.
[<Versioned("v2")>]
[<Route("api/[controller]")>]
type ReportsController() =
    inherit ControllerBase()

    [<HttpGet>]
    member _.Summary() = "summary"
