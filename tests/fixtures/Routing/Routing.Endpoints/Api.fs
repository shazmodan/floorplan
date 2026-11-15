/// Handlers registered with ASP.NET Core endpoint routing (form C).
module Routing.Endpoints.Api

let listOrders () = "orders"

let getOrder (id: int) = $"order {id}"

let createOrder () = "created"

let health () = "ok"
