/// Handlers registered with ASP.NET Core endpoint routing (form C).
module Routing.Endpoints.Api

let listOrders () = "orders"

let getOrder (id: int) = $"order {id}"

let createOrder () = "created"

let replaceOrder (id: int) = $"replaced {id}"

let deleteOrder (id: int) = $"deleted {id}"

let patchOrder (id: int) = $"patched {id}"

let orderOptions () = "GET, POST, PUT, DELETE, PATCH"

let health () = "ok"
