/// A tiny request type and active patterns, as an in-house HTTP package might define them.
module Routing.Listener.Request

type Req = { Method: string; Segments: string list }

let (|GET|_|) (r: Req) = if r.Method = "GET" then Some r.Segments else None

let (|POST|_|) (r: Req) = if r.Method = "POST" then Some r.Segments else None
