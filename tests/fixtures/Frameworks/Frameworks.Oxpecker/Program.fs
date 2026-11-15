/// Oxpecker: a tree of route definitions mapped onto ASP.NET Core endpoint routing.
module Frameworks.Oxpecker.Program

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Oxpecker

let endpoints =
    [ GET [ route "/" (text "home") ]
      subRoute "/api" [
          GET [ route "/orders" Orders.list; routef "/orders/{%i}" Orders.get ]
          POST [ route "/orders" Orders.create ]
          PUT [ routef "/orders/{%i}" Orders.replace ]
          DELETE [ routef "/orders/{%i}" Orders.remove ]
          PATCH [ routef "/orders/{%i}" Orders.patch ]
          OPTIONS [ route "/orders" Orders.options ]
      ] ]

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder args
    builder.Services.AddRouting().AddOxpecker() |> ignore
    let app = builder.Build()
    app.UseRouting().UseOxpecker(endpoints) |> ignore
    app.Run()
    0
