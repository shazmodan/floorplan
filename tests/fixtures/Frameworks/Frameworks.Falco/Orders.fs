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

let replace: HttpHandler = fun ctx -> Response.ofPlainText "replaced" ctx

let remove: HttpHandler = fun ctx -> Response.ofPlainText "deleted" ctx

let patch: HttpHandler = fun ctx -> Response.ofPlainText "patched" ctx

let options: HttpHandler = fun ctx -> Response.ofPlainText "GET, POST, PUT, DELETE, PATCH" ctx
