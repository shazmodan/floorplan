module Routing.Wrapped.Program

open Microsoft.AspNetCore.Builder
open Routing.Library.Web
open Routing.Wrapped.Middleware

/// A router tried after another: FSharp.Core's `Option.orElseWith` runs it, so the routing runs inside a library call.
let thenTry fallback routing =
    fun request -> routing request |> Option.orElseWith (fun () -> fallback request)

[<EntryPoint>]
let main args =
    let app = WebApplication.CreateBuilder(args).Build()

    let routing (request, _) =
        Api.route request |> Option.map Async.enter

    app.UseToolkitRouting(thenTry routing (fun _ -> None), fun ex -> eprintfn "Error in routing: %O" ex)
    app.Run()
    0
