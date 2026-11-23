/// Routes matched with a toolkit's active patterns, the path's prefix taken off before a nested router: every
/// route answers with a shared response value, not by calling a handler function.
module Routing.Wrapped.Api

open Routing.Library.Web
open Routing.Library.Web.Http
open Routing.Library.Web.Routing
open Routing.Library.Web.Tools

let private jsonResponse value =
    let response = JsonResponse value
    { response with Headers = response.Headers |> Map.add "Cache-Control" "no-store" }

let private mockResponse = jsonResponse "\"mock\""

let private adminRoute request =
    match request with
    | GET([ "Principal" ], _)
    | GET([ "Principal"; Int32Str _ ], _)
    | GET([ "Groups" ], MapKey "query" _)
    | GET([ "Roles"; Int32Str _; _ ], _)
    | POST([ "Principal"; Int32Str _; "Delete" ])
    | POST([ "User"; "New" ]) -> Some mockResponse
    | _ -> None

let private apiRoute request =
    match request with
    | GET([ "FindUser"; _ ], _)
    | POST([ "Login" ]) -> Some mockResponse
    | _ -> None

let route (request: HttpRequest) =
    match request with
    | GET([], _) -> Some(StrResponse "API")
    | GET([ "Awesome" ], _) -> Some(jsonResponse "\"awesome\"")
    | OPTIONS _ -> Some(StrResponse "Ok")
    | _ ->
        match request.Url.Path with
        | "Admin" :: path -> adminRoute { request with Url = { request.Url with Path = path } }
        | "Api" :: path -> apiRoute { request with Url = { request.Url with Path = path } }
        | "Shop" :: path -> Shop.route { request with Url = { request.Url with Path = path } }
        | _ -> None
