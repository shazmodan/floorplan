module Routing.Records.Program

open System
open System.Net
open Routing.Records.Request

let serve (listener: HttpListener) =
    let context = listener.GetContext()
    let request = context.Request

    let path =
        request.Url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

    let query =
        request.QueryString.AllKeys |> Seq.map (fun k -> k, request.QueryString[k]) |> Map.ofSeq

    let data =
        { Request =
            { Url = { Uri = request.Url; Path = path }
              HttpMethod = request.HttpMethod
              RemoteAddr = lazy (string request.RemoteEndPoint)
              Headers = Map.empty
              QueryString = query }
          FormData = Map.empty }

    let body =
        match Router.route data with
        | Some response -> Async.RunSynchronously response
        | None -> "404"

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
