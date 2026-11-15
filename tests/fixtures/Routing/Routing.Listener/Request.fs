/// A tiny request type and active patterns, as an in-house HTTP package might define them.
module Routing.Listener.Request

type Req = { Method: string; Segments: string list }

let (|GET|_|) (r: Req) = if r.Method = "GET" then Some r.Segments else None

let (|POST|_|) (r: Req) = if r.Method = "POST" then Some r.Segments else None

let (|PUT|_|) (r: Req) = if r.Method = "PUT" then Some r.Segments else None

let (|DELETE|_|) (r: Req) = if r.Method = "DELETE" then Some r.Segments else None

let (|PATCH|_|) (r: Req) = if r.Method = "PATCH" then Some r.Segments else None

let (|OPTIONS|_|) (r: Req) = if r.Method = "OPTIONS" then Some r.Segments else None
