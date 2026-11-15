/// Negative cases (plan section 9): a route read from configuration and a regular expression built at run
/// time. Both handlers must be found, and neither may get a route.
namespace Routing.Dynamic

open System
open System.Text.RegularExpressions
open Microsoft.AspNetCore.Http

module Admin =
    let run (ctx: HttpContext) = ctx.Response.WriteAsync "admin"

module Reports =
    let run (ctx: HttpContext) = ctx.Response.WriteAsync "reports"

module Router =
    let configured = Environment.GetEnvironmentVariable "ADMIN_ROUTE"
    let pattern = Environment.GetEnvironmentVariable "REPORT_PATTERN"

    let route (ctx: HttpContext) =
        let path = ctx.Request.Path.Value

        if path = configured then Admin.run ctx
        elif Regex(pattern).IsMatch path then Reports.run ctx
        else ctx.Response.WriteAsync "no"
