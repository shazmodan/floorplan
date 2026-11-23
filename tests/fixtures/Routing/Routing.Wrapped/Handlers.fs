/// What the shop's routes call: each gets what its route's patterns bound.
module Routing.Wrapped.Handlers

open Routing.Library.Web.Http

let private json (text: string) = JsonResponse text

let list (kind: string) = json ("[\"" + kind + "\"]")

let show (kind: string) (id: int) =
    if id < 0 then StrResponse "bad id" else json ("{\"" + kind + "\":" + id.ToString() + "}")

let create (kind: string) = json ("{\"created\":\"" + kind + "\"}")

let update (kind: string) (id: int) = json ("{\"updated\":" + id.ToString() + "}")

let remove (kind: string) (id: int) = json ("{\"removed\":" + id.ToString() + "}")

let item (kind: string) (id: int) (itemId: int) = json ("{\"" + kind + "\":" + id.ToString() + ",\"item\":" + itemId.ToString() + "}")
