module Floorplan.Tests.EntryPointTests

open System.Text
open Expecto
open Floorplan.Model
open Floorplan.Cli

let routingModel = lazy (Pipeline.buildModel ignore [] (Fixtures.routing.Force()))

let render (model: AnalysisModel) =
    let b = StringBuilder()

    for e in model.EntryPoints do
        let predicates =
            e.Predicates
            |> List.map (fun p ->
                let index = match p.Index with Some i -> string i | None -> ""
                $"{Json.unionName p.Part}{index} {Json.unionName p.Operator} {p.Value}")
            |> String.concat "; "

        let route = defaultArg e.Route "-"
        let httpMethod = defaultArg e.HttpMethod "-"
        let forms = e.Forms |> List.map Json.unionName |> String.concat ","

        b.AppendLine(
            $"{e.ApplicationId} | {Json.unionName e.Category} | {httpMethod} {route} | {Json.unionName e.Completeness} | {e.HandlerUnit} | {e.HandlerMember} | forms [{forms}] | [{predicates}]"
        )
        |> ignore

    b.ToString()

let private entries (model: AnalysisModel) (app: string) =
    model.EntryPoints |> List.filter (fun e -> e.ApplicationId = app)

let private handlers (model: AnalysisModel) (app: string) =
    entries model app |> List.map (fun e -> e.HandlerMember) |> List.sort

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Entry points"
        [ test "golden: Routing fixture" { Golden.check "routing.entry-points.txt" (render (routingModel.Force())) }

          test "golden: Shop fixture" { Golden.check "shop.entry-points.txt" (render (ExitPointTests.shopModel.Force())) }

          test "endpoint routing gives complete routes, including groups and method lists" {
              let m = routingModel.Force()

              let routes =
                  entries m "Routing.Endpoints"
                  |> List.map (fun e -> defaultArg e.HttpMethod "", defaultArg e.Route "", e.Completeness)
                  |> List.sort

              Expect.equal
                  routes
                  [ "GET", "/api/orders", Completeness.Complete
                    "GET", "/api/orders/{id:int}", Completeness.Complete
                    "GET,HEAD", "/health", Completeness.Complete
                    "POST", "/v2/orders", Completeness.Complete ]
                  "routes"

              let getOrder = entries m "Routing.Endpoints" |> List.find (fun e -> e.HandlerMember.EndsWith "getOrder")
              Expect.equal getOrder.HandlerUnit "Routing.Endpoints:Routing.Endpoints.Api" "handler module"
              Expect.equal getOrder.Forms [ RouteForm.PlatformRouting ] "form C"

              Expect.exists
                  getOrder.Predicates
                  (fun p -> p.Part = RequestPart.PathSegment && p.Operator = PredicateOperator.Parameter && p.Index = Some 2)
                  "route parameter"
          }

          test "hand-written middleware routing: every branch handler is found" {
              let m = routingModel.Force()

              Expect.equal
                  (handlers m "Routing.Middleware")
                  [ "Routing.Middleware.Health::check"
                    "Routing.Middleware.NotFound::handle"
                    "Routing.Middleware.Orders::create"
                    "Routing.Middleware.Orders::show" ]
                  "handlers"
          }

          test "list patterns and active patterns: handlers, not the router or the patterns" {
              let m = routingModel.Force()

              Expect.equal
                  (handlers m "Routing.Listener")
                  [ "Routing.Listener.Orders::create"; "Routing.Listener.Orders::fetch"; "Routing.Listener.Orders::list" ]
                  "handlers"
          }

          test "a raw socket gives an unresolved network entry point" {
              let m = routingModel.Force()

              match entries m "Routing.Socket" with
              | [ e ] ->
                  Expect.equal e.Category EffectCategory.Network "network"
                  Expect.equal e.Completeness Completeness.Unresolved "unresolved"
                  Expect.equal e.HandlerMember "Routing.Socket.Connection::handle" "handler"
              | other -> failtestf "expected one entry point, got %d" other.Length
          }

          test "Shop: minimal API handlers are found across projects" {
              let m = ExitPointTests.shopModel.Force()
              Expect.equal (handlers m "Shop.Web") [ "Shop.Web.Handlers::createOrder"; "Shop.Web.Handlers::listOrders"; "Shop.Web.Handlers::price" ] "handlers"
              Expect.isEmpty (entries m "Shop.Worker") "a worker has no entry points"
          } ]
