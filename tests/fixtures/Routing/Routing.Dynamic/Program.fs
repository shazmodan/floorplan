module Routing.Dynamic.Program

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

[<EntryPoint>]
let main args =
    let app = WebApplication.CreateBuilder(args).Build()
    app.Run(RequestDelegate(fun ctx -> Router.route ctx))
    app.Run()
    0
