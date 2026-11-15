module Frameworks.Falco.Orders

open System.IO
open Falco

let list: HttpHandler = fun ctx -> Response.ofPlainText (File.ReadAllText "orders.txt") ctx

let get: HttpHandler =
    fun ctx ->
        let route = Request.getRoute ctx
        Response.ofPlainText $"""order {route.GetInt "id"}""" ctx

let create: HttpHandler =
    fun ctx ->
        File.AppendAllText("orders.txt", "new")
        Response.ofPlainText "created" ctx
