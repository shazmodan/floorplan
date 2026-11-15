module Frameworks.Oxpecker.Orders

open System.IO
open Oxpecker

let list: EndpointHandler = fun ctx -> ctx.WriteText(File.ReadAllText "orders.txt")

let get (id: int) : EndpointHandler = fun ctx -> ctx.WriteText $"order {id}"

let create: EndpointHandler =
    fun ctx ->
        File.AppendAllText("orders.txt", "new")
        ctx.WriteText "created"
