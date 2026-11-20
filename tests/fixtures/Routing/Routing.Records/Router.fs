module Routing.Records.Router

open Routing.Records.Request

let route (requestData: HttpRequestData) =
    match requestData.Request with
    | GET([ "Session" ], _) -> Session.show () |> Some
    | POST([ "Session" ], query) -> Session.create query |> Some
    | DELETE([ "Session"; id ], _) -> Session.remove id |> Some
    | _ -> None
