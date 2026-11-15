namespace Routing.Middleware

open Microsoft.AspNetCore.Http

module Health =
    let check (ctx: HttpContext) = ctx.Response.WriteAsync "ok"

module Orders =
    let create (ctx: HttpContext) = ctx.Response.WriteAsync "created"

    let show (ctx: HttpContext) (id: string) = ctx.Response.WriteAsync $"order {id}"

module NotFound =
    let handle (ctx: HttpContext) =
        ctx.Response.StatusCode <- 404
        ctx.Response.WriteAsync "not found"
