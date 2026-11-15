/// Hand-written routing inside one ASP.NET Core middleware: conditions in control flow (form A).
module Routing.Middleware.Router

open Microsoft.AspNetCore.Http

let route (ctx: HttpContext) =
    let path = ctx.Request.Path.Value
    let method = ctx.Request.Method

    if method = "GET" && path = "/health" then
        Health.check ctx
    elif method = "POST" && path.StartsWith "/orders" then
        Orders.create ctx
    else
        let segments = path.Split('/', System.StringSplitOptions.RemoveEmptyEntries)

        if method = "GET" && segments.Length = 2 && segments[0] = "orders" then
            Orders.show ctx segments[1]
        else
            NotFound.handle ctx
