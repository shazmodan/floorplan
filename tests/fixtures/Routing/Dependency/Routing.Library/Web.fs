/// A web toolkit as a package might ship it (the shapes of a real one, issue: every route unresolved): a request
/// record the application fills from ASP.NET Core, active patterns made by partially applying inline matchers, a
/// parsing active pattern, responses, and an async map operator.
namespace Routing.Library.Web

open System

module Http =
    type RequestUrl = { Uri: Uri; Path: string list }

    type HttpRequest =
        { Url: RequestUrl
          HttpMethod: string
          ContentType: string
          Headers: Map<string, string>
          QueryString: Map<string, string> }

    type HttpResponse =
        { Status: int
          ContentType: string
          Headers: Map<string, string>
          ResponseWriter: IO.Stream -> unit }

    let createResponse status contentType writer =
        { Status = status; ContentType = contentType; Headers = Map.empty; ResponseWriter = writer }

    let writeText (text: string) (stream: IO.Stream) =
        let bytes = Text.Encoding.UTF8.GetBytes text
        stream.Write(bytes, 0, bytes.Length)

    let StrResponse (text: string) = createResponse 200 "text/plain" (writeText text)

    let JsonResponse (json: string) = createResponse 200 "application/json" (writeText json)

module Routing =
    open Http

    let inline matchRequestWithQuery (httpMethod: string) (request: HttpRequest) =
        if request.HttpMethod = httpMethod then Some(request.Url.Path, request.QueryString) else None

    let inline matchRequestWithoutQuery (httpMethod: string) (request: HttpRequest) =
        if request.HttpMethod = httpMethod then Some request.Url.Path else None

    let (|GET|_|) = matchRequestWithQuery "GET"
    let (|POST|_|) = matchRequestWithoutQuery "POST"
    let (|DELETE|_|) = matchRequestWithoutQuery "DELETE"
    let (|OPTIONS|_|) = matchRequestWithoutQuery "OPTIONS"

module Tools =
    let tryParseInt32 (s: string) =
        match Int32.TryParse s with
        | true, v -> Some v
        | _ -> None

    let (|Int32Str|_|) = tryParseInt32

    let (|MapKey|_|) (key: string) (map: Map<string, string>) = Map.tryFind key map

[<AutoOpen>]
module Operators =
    /// Maps the result of an async computation.
    let (|>>) (computation: Async<'a>) (f: 'a -> 'b) = async { let! x = computation in return f x }

module Async =
    let enter (value: 'a) = async { return value }
