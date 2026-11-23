/// ASP.NET Core middleware that maps the request to the toolkit's record, calls the routing function it is given
/// and writes the response, or calls the next middleware when the router gives none.
module Routing.Wrapped.Middleware

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Routing.Library.Web

let private mapRequest (context: HttpContext) : Http.HttpRequest * HttpContext =
    let request = context.Request
    let uri = System.Uri(Microsoft.AspNetCore.Http.Extensions.UriHelper.GetDisplayUrl request)

    { Url =
        { Uri = uri
          Path = request.Path.Value.Split('/') |> List.ofArray |> List.filter (fun s -> s <> "") }
      HttpMethod = request.Method
      ContentType = request.ContentType
      Headers = request.Headers |> Seq.fold (fun headers h -> Map.add h.Key (string h.Value) headers) Map.empty
      QueryString = request.Query |> Seq.fold (fun qs q -> Map.add q.Key (string q.Value) qs) Map.empty },
    context

let private applyResponse (context: HttpContext) (response: Http.HttpResponse) =
    context.Response.StatusCode <- response.Status
    context.Response.ContentType <- response.ContentType

    for header in response.Headers do
        context.Response.Headers.Append(header.Key, header.Value)

    response.ResponseWriter context.Response.Body

type RouteType = System.Func<HttpContext, RequestDelegate, System.Threading.Tasks.Task>

let useRouting errorHandling (app: IApplicationBuilder) (routing: Http.HttpRequest * HttpContext -> Async<Http.HttpResponse> option) =
    let route: RouteType =
        System.Func<_, _, _>(fun context next ->
            let resp =
                try
                    match context.Response.StatusCode with
                    | 403 -> async { return Http.createResponse 403 "text/plain" (Http.writeText "403 - Forbidden.") } |> Some
                    | _ -> routing (mapRequest context)
                with e ->
                    errorHandling e
                    reraise ()

            match resp with
            | Some aResponse ->
                aResponse |>> (fun res -> applyResponse context res) |> Async.StartAsTask :> System.Threading.Tasks.Task
            | None -> next.Invoke context)

    app.Use(route) |> ignore<IApplicationBuilder>

open System.Runtime.CompilerServices

[<Extension>]
type ApplicationBuilderExtensions() =
    [<Extension>]
    static member inline UseToolkitRouting(app: IApplicationBuilder, routing, errorHandling) = useRouting errorHandling app routing
