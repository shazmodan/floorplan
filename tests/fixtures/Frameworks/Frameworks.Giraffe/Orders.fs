module Frameworks.Giraffe.Orders

open System.IO
open Giraffe

let list: HttpHandler = fun next ctx -> text (File.ReadAllText "orders.txt") next ctx

let get (id: int) : HttpHandler = fun next ctx -> text $"order {id}" next ctx

let create: HttpHandler =
    fun next ctx ->
        File.AppendAllText("orders.txt", "new")
        text "created" next ctx
