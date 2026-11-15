/// Routing by list patterns over path segments, with active patterns for the method (plan section 6.5).
module Routing.Listener.Router

open Routing.Listener.Request

let route (r: Req) =
    match r with
    | GET [ "api"; "orders" ] -> Orders.list ()
    | POST [ "api"; "orders" ] -> Orders.create ()
    | OPTIONS [ "api"; "orders" ] -> Orders.options ()
    | GET [ "api"; "orders"; id ] -> Orders.fetch id
    | PUT [ "api"; "orders"; id ] -> Orders.replace id
    | DELETE [ "api"; "orders"; id ] -> Orders.remove id
    | PATCH [ "api"; "orders"; id ] -> Orders.patch id
    | _ -> "404"
