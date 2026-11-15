/// Hand-written routing inside one ASP.NET Core middleware: conditions in control flow (form A). Methods are
/// compared as strings, with a match, and through the platform's HttpMethods helpers.
module Routing.Middleware.Router

open Microsoft.AspNetCore.Http

let route (ctx: HttpContext) =
    let path = ctx.Request.Path.Value
    let method = ctx.Request.Method

    if method = "GET" && path = "/health" then
        Health.check ctx
    elif method = "POST" && path.StartsWith "/orders" then
        Orders.create ctx
    elif HttpMethods.IsOptions method && path = "/orders" then
        Orders.options ctx
    else
        let segments = path.Split('/', System.StringSplitOptions.RemoveEmptyEntries)

        if segments.Length = 2 && segments[0] = "orders" then
            match method with
            | "GET" -> Orders.show ctx segments[1]
            | "PUT" -> Orders.replace ctx segments[1]
            | "DELETE" -> Orders.remove ctx segments[1]
            | _ when HttpMethods.IsPatch method -> Orders.patch ctx segments[1]
            | _ -> NotFound.handle ctx
        else
            NotFound.handle ctx
