module Routing.Listener.Program

open System
open System.Net
open Routing.Listener.Request

let serve (listener: HttpListener) =
    let context = listener.GetContext()
    let request = context.Request

    let segments =
        request.Url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

    let body = Router.route { Method = request.HttpMethod; Segments = segments }
    let bytes = Text.Encoding.UTF8.GetBytes body
    context.Response.OutputStream.Write(bytes, 0, bytes.Length)
    context.Response.Close()

[<EntryPoint>]
let main _ =
    let listener = new HttpListener()
    listener.Prefixes.Add "http://localhost:8080/"
    listener.Start()

    while true do
        serve listener

    0
