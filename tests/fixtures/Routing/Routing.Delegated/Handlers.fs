namespace Routing.Delegated

open Microsoft.AspNetCore.Http

module Health =
    let check (ctx: HttpContext) = ctx.Response.WriteAsync "ok"

module Orders =
    let create (ctx: HttpContext) = ctx.Response.WriteAsync "created"

    let show (ctx: HttpContext) (id: string) = ctx.Response.WriteAsync $"order {id}"

    let replace (ctx: HttpContext) (id: string) = ctx.Response.WriteAsync $"replaced {id}"

    let remove (ctx: HttpContext) (id: string) = ctx.Response.WriteAsync $"deleted {id}"

    let patch (ctx: HttpContext) (id: string) = ctx.Response.WriteAsync $"patched {id}"

    let options (ctx: HttpContext) = ctx.Response.WriteAsync "GET, POST, PUT, DELETE, PATCH"

module NotFound =
    let handle (ctx: HttpContext) =
        ctx.Response.StatusCode <- 404
        ctx.Response.WriteAsync "not found"
