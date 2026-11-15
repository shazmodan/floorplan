/// Routing by table lookup (form B): handlers stored next to constant paths, looked up with the request path.
module Routing.Table.Router

open System.Collections.Generic

let table: Dictionary<string, unit -> string> =
    let d = Dictionary()
    d["/orders"] <- Handlers.orders
    d["/health"] <- Handlers.health
    d

let byMap = Map.ofList [ "/users", Handlers.users ]

let route (path: string) =
    match table.TryGetValue path with
    | true, handler -> handler ()
    | _ ->
        match Map.tryFind path byMap with
        | Some handler -> handler ()
        | None -> "404"
