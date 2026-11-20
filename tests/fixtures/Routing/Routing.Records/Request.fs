/// A request record with the path nested in a URL record, and active patterns made by partially applying an
/// inline matcher that hands back the path and the query string together (issue #17).
module Routing.Records.Request

open System

type RequestUrl = { Uri: Uri; Path: string list }

type HttpRequest =
    { Url: RequestUrl
      HttpMethod: string
      RemoteAddr: Lazy<string>
      Headers: Map<string, string>
      QueryString: Map<string, string> }

type HttpRequestData = { Request: HttpRequest; FormData: Map<string, string array> }

let inline matchRequestWithQuery (httpMethod: string) (request: HttpRequest) =
    if request.HttpMethod = httpMethod then
        Some(request.Url.Path, request.QueryString)
    else
        None

let (|GET|_|) = matchRequestWithQuery "GET"

let (|POST|_|) = matchRequestWithQuery "POST"

let (|DELETE|_|) = matchRequestWithQuery "DELETE"
