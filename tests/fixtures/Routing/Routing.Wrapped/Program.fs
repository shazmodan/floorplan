module Routing.Wrapped.Program

open Microsoft.AspNetCore.Builder
open Routing.Library.Web
open Routing.Wrapped.Middleware

[<EntryPoint>]
let main args =
    let app = WebApplication.CreateBuilder(args).Build()

    let routing (request, _) =
        Api.route request |> Option.map Async.enter

    app.UseToolkitRouting(routing, fun ex -> eprintfn "Error in routing: %O" ex)
    app.Run()
    0
