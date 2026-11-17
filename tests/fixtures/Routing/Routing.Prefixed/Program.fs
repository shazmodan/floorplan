/// MVC controllers under a route prefix convention.
module Routing.Prefixed.Program

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder args
    builder.Services.AddControllers(fun options -> options.Conventions.Add(RoutePrefixConvention("api"))) |> ignore
    let app = builder.Build()
    app.MapControllers() |> ignore
    app.Run()
    0
