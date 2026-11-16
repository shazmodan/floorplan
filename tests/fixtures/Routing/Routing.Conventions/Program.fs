/// MVC controllers under application model conventions.
module Routing.Conventions.Program

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder args
    builder.Services.AddControllers(fun options -> options.Conventions.Add(AuditConvention())) |> ignore
    let app = builder.Build()
    app.MapControllers() |> ignore
    app.Run()
    0
