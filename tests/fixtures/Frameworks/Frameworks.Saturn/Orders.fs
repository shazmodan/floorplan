module Frameworks.Saturn.Orders

open System.IO
open Giraffe

let list: HttpHandler = fun next ctx -> text (File.ReadAllText "orders.txt") next ctx

let get (id: int) : HttpHandler = fun next ctx -> text $"order {id}" next ctx

let create: HttpHandler =
    fun next ctx ->
        File.AppendAllText("orders.txt", "new")
        text "created" next ctx

let replace (id: int) : HttpHandler = fun next ctx -> text $"replaced {id}" next ctx

let remove (id: int) : HttpHandler = fun next ctx -> text $"deleted {id}" next ctx

let patch (id: int) : HttpHandler = fun next ctx -> text $"patched {id}" next ctx

let options: HttpHandler = fun next ctx -> text "GET, POST, PUT, DELETE, PATCH" next ctx
