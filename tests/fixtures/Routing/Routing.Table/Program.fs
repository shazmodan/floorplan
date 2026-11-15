module Routing.Table.Program

open System.Net

[<EntryPoint>]
let main _ =
    let listener = new HttpListener()
    listener.Prefixes.Add "http://localhost:8081/"
    listener.Start()

    while true do
        let context = listener.GetContext()
        let body = Router.route context.Request.Url.AbsolutePath
        let bytes = System.Text.Encoding.UTF8.GetBytes body
        context.Response.OutputStream.Write(bytes, 0, bytes.Length)
        context.Response.Close()

    0
