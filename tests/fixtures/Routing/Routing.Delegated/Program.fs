/// Routing.Middleware with its router in a dependency: only the handlers are user code.
module Routing.Delegated.Program

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Routing.Library

let handlers: Router.Handlers =
    { Health = Health.check
      CreateOrder = Orders.create
      OrderOptions = Orders.options
      ShowOrder = Orders.show
      ReplaceOrder = Orders.replace
      RemoveOrder = Orders.remove
      PatchOrder = Orders.patch
      NotFound = NotFound.handle }

[<EntryPoint>]
let main args =
    let app = WebApplication.CreateBuilder(args).Build()
    app.Run(RequestDelegate(fun ctx -> Router.route handlers ctx))
    app.Run()
    0
