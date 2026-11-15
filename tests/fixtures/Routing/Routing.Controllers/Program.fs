/// MVC controllers with attribute routing, mapped by MapControllers.
module Routing.Controllers.Program

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder args
    builder.Services.AddControllers() |> ignore
    let app = builder.Build()
    app.MapControllers() |> ignore
    app.Run()
    0
