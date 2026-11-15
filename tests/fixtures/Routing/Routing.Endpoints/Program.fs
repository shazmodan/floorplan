module Routing.Endpoints.Program

open System
open Microsoft.AspNetCore.Builder

[<EntryPoint>]
let main args =
    let app = WebApplication.CreateBuilder(args).Build()

    app.MapGet("/api/orders", Func<string>(Api.listOrders)) |> ignore
    app.MapGet("/api/orders/{id:int}", Func<int, string>(Api.getOrder)) |> ignore

    let v2 = app.MapGroup("/v2")
    v2.MapPost("/orders", Func<string>(Api.createOrder)) |> ignore

    app.MapMethods("/health", [| "GET"; "HEAD" |], Func<string>(Api.health)) |> ignore

    app.Run()
    0
