module RouterSample.Router

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http

// Style 1: list pattern over split path, with an active pattern for the method.
type Req = { Method: string; Segments: string list }

let (|GET|_|) (r: Req) = if r.Method = "GET" then Some r.Segments else None
let (|POST|_|) (r: Req) = if r.Method = "POST" then Some r.Segments else None

module Orders =
    let list () = File.ReadAllText "orders.txt"
    let create () = File.WriteAllText("orders.txt", "x"); "ok"
    let fetch (id: string) =
        use c = new HttpClient()
        c.GetStringAsync("https://example.org/orders/" + id).Result

let route (r: Req) =
    match r with
    | GET [ "api"; "orders" ] -> Orders.list ()
    | POST [ "api"; "orders" ] -> Orders.create ()
    | GET [ "api"; "orders"; id ] -> Orders.fetch id
    | _ -> "404"

// Style 2: StartsWith control flow.
let route2 (path: string) =
    if path.StartsWith "/api/orders" then Orders.list ()
    elif String.Equals(path, "/health") then "ok"
    else "404"

// Style 3: table lookup.
let table : Dictionary<string, unit -> string> =
    let d = Dictionary()
    d.["/api/orders"] <- Orders.list
    d.["/api/create"] <- Orders.create
    d

let route3 (path: string) =
    match table.TryGetValue path with
    | true, h -> h ()
    | _ -> "404"

// Entry: HttpListener feeding route.
let serve () =
    let l = new HttpListener()
    l.Start()
    let ctx = l.GetContext()
    let req = ctx.Request
    let segs = req.Url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray
    let body = route { Method = req.HttpMethod; Segments = segs }
    let async' = async { return route2 req.RawUrl }
    body + Async.RunSynchronously async' + route3 req.RawUrl
