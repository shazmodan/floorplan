module Frameworks.Oxpecker.Orders

open System.IO
open Oxpecker

let list: EndpointHandler = fun ctx -> ctx.WriteText(File.ReadAllText "orders.txt")

let get (id: int) : EndpointHandler = fun ctx -> ctx.WriteText $"order {id}"

let create: EndpointHandler =
    fun ctx ->
        File.AppendAllText("orders.txt", "new")
        ctx.WriteText "created"

let replace (id: int) : EndpointHandler = fun ctx -> ctx.WriteText $"replaced {id}"

let remove (id: int) : EndpointHandler = fun ctx -> ctx.WriteText $"deleted {id}"

let patch (id: int) : EndpointHandler = fun ctx -> ctx.WriteText $"patched {id}"

let options: EndpointHandler = fun ctx -> ctx.WriteText "GET, POST, PUT, DELETE, PATCH"
