module Routing.Endpoints.Program

open System
open Microsoft.AspNetCore.Builder

[<EntryPoint>]
let main args =
    let app = WebApplication.CreateBuilder(args).Build()

    app.MapGet("/api/orders", Func<string>(Api.listOrders)) |> ignore
    app.MapGet("/api/orders/{id:int}", Func<int, string>(Api.getOrder)) |> ignore
    app.MapPut("/api/orders/{id:int}", Func<int, string>(Api.replaceOrder)) |> ignore
    app.MapDelete("/api/orders/{id:int}", Func<int, string>(Api.deleteOrder)) |> ignore
    app.MapPatch("/api/orders/{id:int}", Func<int, string>(Api.patchOrder)) |> ignore
    app.MapMethods("/api/orders", [| "OPTIONS" |], Func<string>(Api.orderOptions)) |> ignore

    let v2 = app.MapGroup("/v2")
    v2.MapPost("/orders", Func<string>(Api.createOrder)) |> ignore

    app.MapMethods("/health", [| "GET"; "HEAD" |], Func<string>(Api.health)) |> ignore

    app.Run()
    0
