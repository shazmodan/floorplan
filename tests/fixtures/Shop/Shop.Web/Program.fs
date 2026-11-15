module Shop.Web.Program

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder args
    let app = builder.Build()

    app.MapGet("/api/orders", Func<string>(Handlers.listOrders)) |> ignore
    app.MapPost("/api/orders/{item}", Func<string, string>(Handlers.createOrder)) |> ignore
    app.MapGet("/api/prices/{item}", Func<string, Threading.Tasks.Task<string>>(Handlers.price)) |> ignore

    app.Run()
    0
