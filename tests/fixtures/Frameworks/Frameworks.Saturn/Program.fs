/// Saturn: routers written as computation expressions on top of Giraffe, nested with forward.
module Frameworks.Saturn.Program

open Giraffe
open Saturn

let orders =
    router {
        get "" Orders.list
        getf "/%i" Orders.get
        post "" Orders.create
        putf "/%i" Orders.replace
        deletef "/%i" Orders.remove
        patchf "/%i" Orders.patch
    }

let api =
    router {
        forward "/orders" orders
        forward "/orders" (OPTIONS >=> route "" >=> Orders.options)
    }

let appRouter =
    router {
        get "/" (text "home")
        forward "/api" api
    }

let app = application { use_router appRouter }

[<EntryPoint>]
let main _ =
    run app
    0
