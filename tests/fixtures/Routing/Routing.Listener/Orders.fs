module Routing.Listener.Orders

let list () = "all orders"

let create () = "created"

let fetch (id: string) = $"order {id}"

let replace (id: string) = $"replaced {id}"

let remove (id: string) = $"deleted {id}"

let patch (id: string) = $"patched {id}"

let options () = "GET, POST, PUT, DELETE, PATCH"
