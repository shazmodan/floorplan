/// Real frameworks as test inputs only (plan section 9): what works on the fixture matrix must work in the wild.
module Floorplan.Tests.FrameworkTests

open Expecto
open Floorplan.Model
open Floorplan.Cli

let frameworksModel =
    lazy (Pipeline.buildModel ignore Caching.Store.Disabled [] [] (Fixtures.frameworks.Force()))

let frameworksModelNet10 =
    lazy (Pipeline.buildModel ignore Caching.Store.Disabled [] [] (Fixtures.frameworksNet10.Force()))

let apps = [ "Frameworks.Giraffe"; "Frameworks.Falco"; "Frameworks.Oxpecker"; "Frameworks.Saturn" ]

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Frameworks"
        [ test "handler effects are found in every framework" {
              let m = frameworksModel.Force()

              for app in apps do
                  let inApp = m.ExitPoints |> List.filter (fun x -> x.Unit = $"{app}:{app}.Orders")
                  Expect.exists inApp (fun x -> x.Category = EffectCategory.FilesystemRead) $"{app} reads"
                  Expect.exists inApp (fun x -> x.Category = EffectCategory.FilesystemWrite) $"{app} writes"
          } ]

let private routes (app: string) =
    frameworksModel.Force().EntryPoints
    |> List.filter (fun e -> e.ApplicationId = app)
    |> List.map (fun e -> defaultArg e.HttpMethod "-", defaultArg e.Route "-", e.HandlerMember, e.Completeness)
    |> List.sort

[<Tests>]
let routingTests =
    testSequenced
    <| testList
        "Frameworks: routing inside the framework"
        [ test "golden: framework fixtures" {
              Golden.check "frameworks.entry-points.txt" (EntryPointTests.render (frameworksModel.Force()))
          }

          test "golden: framework fixtures on net10.0 give the net8.0 results" {
              let m = frameworksModelNet10.Force()
              Expect.isTrue (m.Projects |> List.forall (fun p -> p.TargetFramework = "net10.0")) "every project on net10.0"
              Golden.check "frameworks.entry-points.txt" (EntryPointTests.render m)
          }

          test "Falco: routes from an endpoint data source, with methods from endpoint metadata" {
              Expect.equal
                  (routes "Frameworks.Falco")
                  [ "DELETE", "/api/orders/{id:int}", "Frameworks.Falco.Orders::remove", Completeness.Complete
                    "GET", "/", "Frameworks.Falco.Program (response built by a library function)", Completeness.Complete
                    "GET", "/api/orders", "Frameworks.Falco.Orders::list", Completeness.Complete
                    "GET", "/api/orders/{id:int}", "Frameworks.Falco.Orders::get", Completeness.Complete
                    "OPTIONS", "/api/orders", "Frameworks.Falco.Orders::options", Completeness.Complete
                    "PATCH", "/api/orders/{id:int}", "Frameworks.Falco.Orders::patch", Completeness.Complete
                    "POST", "/api/orders", "Frameworks.Falco.Orders::create", Completeness.Complete
                    "PUT", "/api/orders/{id:int}", "Frameworks.Falco.Orders::replace", Completeness.Complete ]
                  "Falco"
          }

          test "Giraffe: routes recovered from composed function values, routef patterns named after the handler" {
              Expect.equal
                  (routes "Frameworks.Giraffe")
                  [ "DELETE", "/api/orders", "Frameworks.Giraffe.Orders::removeAll", Completeness.Complete
                    "GET", "/", "Frameworks.Giraffe.Program (response built by a library function)", Completeness.Complete
                    "GET", "/api/orders", "Frameworks.Giraffe.Orders::list", Completeness.Complete
                    "GET", "/api/orders/{id}", "Frameworks.Giraffe.Orders::get", Completeness.Complete
                    "OPTIONS", "/api/orders", "Frameworks.Giraffe.Orders::options", Completeness.Complete
                    "PATCH", "/api/orders", "Frameworks.Giraffe.Orders::patchAll", Completeness.Complete
                    "POST", "/api/orders", "Frameworks.Giraffe.Orders::create", Completeness.Complete
                    "PUT", "/api/orders/{id}", "Frameworks.Giraffe.Orders::replace", Completeness.Complete ]
                  "Giraffe"
          }

          test "Oxpecker: a route tree on endpoint routing, routef templates built by reflection" {
              Expect.equal
                  (routes "Frameworks.Oxpecker")
                  [ "DELETE", "/api/orders/{id}", "Frameworks.Oxpecker.Orders::remove", Completeness.Complete
                    "GET", "/", "Frameworks.Oxpecker.Program (response built by a library function)", Completeness.Complete
                    "GET", "/api/orders", "Frameworks.Oxpecker.Orders::list", Completeness.Complete
                    "GET", "/api/orders/{id}", "Frameworks.Oxpecker.Orders::get", Completeness.Complete
                    "OPTIONS", "/api/orders", "Frameworks.Oxpecker.Orders::options", Completeness.Complete
                    "PATCH", "/api/orders/{id}", "Frameworks.Oxpecker.Orders::patch", Completeness.Complete
                    "POST", "/api/orders", "Frameworks.Oxpecker.Orders::create", Completeness.Complete
                    "PUT", "/api/orders/{id}", "Frameworks.Oxpecker.Orders::replace", Completeness.Complete ]
                  "Oxpecker"
          }

          test "Saturn: nested routers built by computation expressions, forwarded under prefixes" {
              Expect.equal
                  (routes "Frameworks.Saturn")
                  [ "-", "-", "Frameworks.Saturn.Orders::options", Completeness.Unresolved
                    "DELETE", "/api/orders/{id}", "Frameworks.Saturn.Orders::remove", Completeness.Complete
                    "GET", "/", "Frameworks.Saturn.Program (response built by a library function)", Completeness.Complete
                    "GET", "/api/orders", "Frameworks.Saturn.Orders::list", Completeness.Complete
                    "GET", "/api/orders/{id}", "Frameworks.Saturn.Orders::get", Completeness.Complete
                    "PATCH", "/api/orders/{id}", "Frameworks.Saturn.Orders::patch", Completeness.Complete
                    "POST", "/api/orders", "Frameworks.Saturn.Orders::create", Completeness.Complete
                    "PUT", "/api/orders/{id}", "Frameworks.Saturn.Orders::replace", Completeness.Complete ]
                  "Saturn (OPTIONS, tried after the whole orders router, runs out of budget: reported unresolved)"
          }

          test "every handler is reported in every framework, and no route is wrong" {
              let m = frameworksModel.Force()

              for app in apps do
                  let handlers = m.EntryPoints |> List.filter (fun e -> e.ApplicationId = app) |> List.map (fun e -> e.HandlerMember)

                  for h in [ "list"; "get"; "create"; "options" ] do
                      Expect.exists handlers (fun x -> x.EndsWith("Orders::" + h)) $"{app} reports Orders.{h}"

                  // The only routes the fixtures define.
                  let allowed =
                      set
                          [ yield "GET /"
                            yield "GET /api/orders"
                            yield "POST /api/orders"
                            yield "OPTIONS /api/orders"
                            yield "DELETE /api/orders"
                            yield "PATCH /api/orders"
                            for m in [ "GET"; "PUT"; "DELETE"; "PATCH" ] do
                                yield $"{m} /api/orders/{{id:int}}"
                                yield $"{m} /api/orders/{{id}}" ]

                  for e in m.EntryPoints |> List.filter (fun e -> e.ApplicationId = app && e.Route.IsSome) do
                      let httpMethod = defaultArg e.HttpMethod "?"
                      let route = $"{httpMethod} {e.Route.Value}"
                      Expect.isTrue (allowed.Contains route) $"{app}: unexpected route {route}"

                      if e.Route = Some "/api/orders" then
                          let expected =
                              match e.HttpMethod with
                              | Some "POST" -> "create"
                              | Some "OPTIONS" -> "options"
                              | Some "DELETE" -> "removeAll"
                              | Some "PATCH" -> "patchAll"
                              | _ -> "list"

                          Expect.stringEnds e.HandlerMember ("Orders::" + expected) $"{app}: {route} handler"
          } ]
