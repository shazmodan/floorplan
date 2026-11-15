module Routing.Socket.Program

open System.Net
open System.Net.Sockets

[<EntryPoint>]
let main _ =
    let listener = TcpListener(IPAddress.Loopback, 9000)
    listener.Start()

    while true do
        let client = listener.AcceptTcpClient()
        Connection.handle client

    0
