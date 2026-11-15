/// Routing by list patterns over path segments, with active patterns for the method (plan section 6.5).
module Routing.Listener.Router

open Routing.Listener.Request

let route (r: Req) =
    match r with
    | GET [ "api"; "orders" ] -> Orders.list ()
    | POST [ "api"; "orders" ] -> Orders.create ()
    | GET [ "api"; "orders"; id ] -> Orders.fetch id
    | _ -> "404"
