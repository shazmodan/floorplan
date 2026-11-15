/// Real frameworks as test inputs only (plan section 9): what works on the fixture matrix must work in the wild.
module Floorplan.Tests.FrameworkTests

open Expecto
open Floorplan.Model
open Floorplan.Cli

let frameworksModel =
    lazy (Pipeline.buildModel ignore Caching.Store.Disabled [] [] (Fixtures.frameworks.Force()))

let apps = [ "Frameworks.Giraffe"; "Frameworks.Falco"; "Frameworks.Oxpecker" ]

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

          test "Falco: routes from an endpoint data source, with methods from endpoint metadata" {
              Expect.equal
                  (routes "Frameworks.Falco")
                  [ "GET", "/", "Frameworks.Falco.Program (response built by a library function)", Completeness.Complete
                    "GET", "/api/orders", "Frameworks.Falco.Orders::list", Completeness.Complete
                    "GET", "/api/orders/{id:int}", "Frameworks.Falco.Orders::get", Completeness.Complete
                    "POST", "/api/orders", "Frameworks.Falco.Orders::create", Completeness.Complete ]
                  "Falco"
          }

          test "Giraffe: a route recovered from composed function values" {
              let giraffe = routes "Frameworks.Giraffe"
              Expect.contains giraffe ("GET", "/api/orders", "Frameworks.Giraffe.Orders::list", Completeness.Complete) "list"
          }

          test "every handler is reported in every framework, and no route is wrong" {
              let m = frameworksModel.Force()

              for app in apps do
                  let handlers = m.EntryPoints |> List.filter (fun e -> e.ApplicationId = app) |> List.map (fun e -> e.HandlerMember)

                  for h in [ "list"; "get"; "create" ] do
                      Expect.exists handlers (fun x -> x.EndsWith("Orders::" + h)) $"{app} reports Orders.{h}"

                  // The only routes the fixtures define.
                  let allowed =
                      set [ "GET /"; "GET /api/orders"; "POST /api/orders"; "GET /api/orders/{id:int}"; "GET /api/orders/{param}" ]

                  for e in m.EntryPoints |> List.filter (fun e -> e.ApplicationId = app && e.Route.IsSome) do
                      let httpMethod = defaultArg e.HttpMethod "?"
                      let route = $"{httpMethod} {e.Route.Value}"
                      Expect.isTrue (allowed.Contains route) $"{app}: unexpected route {route}"

                      if e.Route = Some "/api/orders" then
                          let expected = if e.HttpMethod = Some "POST" then "create" else "list"
                          Expect.stringEnds e.HandlerMember ("Orders::" + expected) $"{app}: {route} handler"
          } ]
