/// Minimal API shapes beyond direct MapGet calls: groups handed to a function, nested groups, lambdas,
/// endpoint convention chains and a route built at run time.
module Routing.Endpoints.Admin

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Routing

let listUsers () = "users"

let getUser (id: int) = $"user {id}"

/// Routes added to a group that the caller created.
let mapUsers (group: RouteGroupBuilder) =
    group.MapGet("/users", Func<string>(listUsers)) |> ignore
    group.MapGet("/users/{id:int}", Func<int, string>(getUser)).WithName("GetUser") |> ignore

/// A nested group, lambdas, and convention chains.
let map (app: IEndpointRouteBuilder) =
    let admin = app.MapGroup("/admin")
    mapUsers admin

    let reports = admin.MapGroup("/reports")
    reports.MapGet("/daily", Func<string>(fun () -> "daily")) |> ignore

    reports
        .MapPost("/rebuild", Func<string>(fun () -> "rebuilding"))
        .RequireAuthorization()
        .WithName("RebuildReports")
    |> ignore

/// A route template that is not a constant.
let mapDynamic (app: IEndpointRouteBuilder) (prefix: string) =
    app.MapGet(prefix + "/status", Func<string>(fun () -> "up")) |> ignore
