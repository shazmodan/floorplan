/// A handler that branches on request data itself (GitHub issue 1): the branch is the handler's own logic, not
/// routing, so the logger it calls on one branch is not an entry point.
module Routing.Endpoints.Verify

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

type VerifyRequest = { Code: string }

module Logger =
    let mutable last = ""
    let warn (message: string) = last <- message

let private verifyResponse (request: VerifyRequest) =
    if request.Code = "" then Results.BadRequest "No code" else Results.Ok request.Code

let verifyEndpoint (ctx: HttpContext) : Task<IResult> =
    task {
        match ctx.Request.ContentType with
        | "application/json" ->
            let! request = ctx.Request.ReadFromJsonAsync<VerifyRequest>(ctx.RequestAborted)
            return verifyResponse request
        | _ ->
            Logger.warn "Invalid content type"
            return Results.BadRequest "Invalid content type"
    }
