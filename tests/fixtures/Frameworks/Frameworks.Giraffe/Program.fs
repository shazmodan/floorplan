/// Giraffe: routes are composed function values (combinators), run by one ASP.NET Core middleware.
module Frameworks.Giraffe.Program

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Giraffe

let webApp =
    choose [
        GET >=> route "/" >=> text "home"
        GET >=> route "/api/orders" >=> Orders.list
        GET >=> routef "/api/orders/%i" Orders.get
        POST >=> route "/api/orders" >=> Orders.create
    ]

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder args
    builder.Services.AddGiraffe() |> ignore
    let app = builder.Build()
    app.UseGiraffe webApp
    app.Run()
    0
