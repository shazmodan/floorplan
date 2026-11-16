/// MVC controllers reached by conventional routes, an area route and attribute routes.
module Routing.Conventional.Program

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder args
    builder.Services.AddControllers() |> ignore
    let app = builder.Build()
    app.MapControllers() |> ignore
    app.MapAreaControllerRoute("admin", "Admin", "Admin/{controller=Home}/{action=Index}/{id?}") |> ignore
    app.MapDefaultControllerRoute() |> ignore
    app.Run()
    0
