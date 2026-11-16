/// Giraffe: routes are composed function values (combinators), run by one ASP.NET Core middleware.
module Frameworks.Giraffe.Program

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Serilog

let webApp =
    choose [
        GET >=> route "/" >=> text "home"
        GET >=> route "/api/orders" >=> Orders.list
        GET >=> routef "/api/orders/%i" Orders.get
        POST >=> route "/api/orders" >=> Orders.create
        PUT >=> routef "/api/orders/%i" Orders.replace
        DELETE >=> route "/api/orders" >=> Orders.removeAll
        PATCH >=> route "/api/orders" >=> Orders.patchAll
        OPTIONS >=> route "/api/orders" >=> Orders.options
    ]

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder args
    builder.Services.AddGiraffe() |> ignore

    // A logging library configured with a callback: it runs at startup and handles no request, so it must not be
    // reported as an entry point.
    builder.Host.UseSerilog(fun _ (configuration: LoggerConfiguration) ->
        configuration.MinimumLevel.Information().WriteTo.Console() |> ignore)
    |> ignore

    let app = builder.Build()
    app.UseGiraffe webApp
    app.Run()
    0
