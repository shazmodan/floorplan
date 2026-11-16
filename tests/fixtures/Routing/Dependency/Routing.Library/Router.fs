/// The routing code of Routing.Middleware, moved into a library the application depends on (plan section 9):
/// the same conditions in control flow (form A), with the application's handlers passed in.
module Routing.Library.Router

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

type Handlers =
    { Health: HttpContext -> Task
      CreateOrder: HttpContext -> Task
      OrderOptions: HttpContext -> Task
      ShowOrder: HttpContext -> string -> Task
      ReplaceOrder: HttpContext -> string -> Task
      RemoveOrder: HttpContext -> string -> Task
      PatchOrder: HttpContext -> string -> Task
      NotFound: HttpContext -> Task }

let route (handlers: Handlers) (ctx: HttpContext) =
    let path = ctx.Request.Path.Value
    let method = ctx.Request.Method

    if method = "GET" && path = "/health" then
        handlers.Health ctx
    elif method = "POST" && path.StartsWith "/orders" then
        handlers.CreateOrder ctx
    elif HttpMethods.IsOptions method && path = "/orders" then
        handlers.OrderOptions ctx
    else
        let segments = path.Split('/', System.StringSplitOptions.RemoveEmptyEntries)

        if segments.Length = 2 && segments[0] = "orders" then
            match method with
            | "GET" -> handlers.ShowOrder ctx segments[1]
            | "PUT" -> handlers.ReplaceOrder ctx segments[1]
            | "DELETE" -> handlers.RemoveOrder ctx segments[1]
            | _ when HttpMethods.IsPatch method -> handlers.PatchOrder ctx segments[1]
            | _ -> handlers.NotFound ctx
        else
            handlers.NotFound ctx
