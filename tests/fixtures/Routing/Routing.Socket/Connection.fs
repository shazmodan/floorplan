/// Hand-written protocol handling on a raw socket: the handler is found, the route is not (plan section 6.7).
module Routing.Socket.Connection

open System.IO
open System.Net.Sockets

let handle (client: TcpClient) =
    use reader = new StreamReader(client.GetStream())
    let line = reader.ReadLine()
    use writer = new StreamWriter(client.GetStream())
    writer.WriteLine("echo " + line)
