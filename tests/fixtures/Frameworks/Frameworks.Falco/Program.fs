/// Falco: routes are data (a list of endpoints) that the framework hands to ASP.NET Core routing.
module Frameworks.Falco.Program

open Microsoft.AspNetCore.Builder
open Falco
open Falco.Routing

let endpoints =
    [ get "/" (Response.ofPlainText "home")
      get "/api/orders" Orders.list
      get "/api/orders/{id:int}" Orders.get
      post "/api/orders" Orders.create
      put "/api/orders/{id:int}" Orders.replace
      delete "/api/orders/{id:int}" Orders.remove
      patch "/api/orders/{id:int}" Orders.patch
      options "/api/orders" Orders.options ]

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder args
    let app = builder.Build()
    app.UseRouting().UseFalco(endpoints) |> ignore
    app.Run()
    0
