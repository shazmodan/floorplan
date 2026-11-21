module Floorplan.Tests.C4Tests

open Expecto
open Floorplan.Model
open Floorplan.Architecture

let private render (model: AnalysisModel) =
    (C4.describe model.Architecture |> String.concat "\n") + "\n"

let private view (model: AnalysisModel) (id: string) =
    model.Architecture.Views |> List.find (fun v -> v.Id = id)

/// The relationships a view shows into an element, as (from, description).
let private into (v: C4View) (target: string) =
    v.Relationships |> List.filter (fun r -> r.To = target) |> List.map (fun r -> r.From, r.Description)

let private named (model: AnalysisModel) (kind: C4Kind) =
    model.Architecture.Elements |> List.filter (fun e -> e.Kind = kind) |> List.map (fun e -> e.Name) |> List.sort

[<Tests>]
let tests =
    testList
        "C4 model"
        [ test "golden: Shop fixture" { Golden.check "shop.c4.txt" (render (ExitPointTests.shopModel.Force())) }

          test "golden: C# fixtures" { Golden.check "csharp.c4.txt" (render (CSharpTests.csharpModel.Force())) }

          test "system context: the solution, who calls it, and what it uses, named by what the analysis found" {
              let m = ExitPointTests.shopModel.Force()

              Expect.equal (named m C4Kind.SoftwareSystem) [ "Shop" ] "the solution is the system"
              Expect.equal (named m C4Kind.Person) [ "User" ] "HTTP routes have a user"

              Expect.equal
                  (named m C4Kind.ExternalSystem)
                  [ "File system"; "Operating system"; "prices.example.org" ]
                  "the host of an absolute URL, files and processes; not the environment, hidden by default, nor the web application the worker calls"

              let context = view m "context"
              Expect.contains (into context "external:http:prices.example.org") (C4.SystemId, "sends HTTP requests") "the system calls the price service"
              Expect.equal (into context C4.SystemId) [ "person:user", "sends HTTP requests to" ] "the user calls the system"
              Expect.equal context.Elements.Head C4.SystemId "the system, and what is around it"
          }

          test "containers: the applications, what they are, and one calling another's route" {
              let m = ExitPointTests.shopModel.Force()
              let containers = m.Architecture.Elements |> List.filter (fun e -> e.Kind = C4Kind.Container)

              Expect.equal
                  (containers |> List.map (fun c -> c.Name, c.Tags, c.Technology))
                  [ "Shop.Web", [ "web application" ], Some "F# · net10.0"
                    "Shop.Worker", [ "background worker" ], Some "F# · net8.0" ]
                  "applications are containers; the libraries they reference are not"

              let containers = view m "containers"
              let web = C4.containerId "Shop.Web"

              Expect.contains
                  (into containers web)
                  (C4.containerId "Shop.Worker", "sends HTTP requests to /api/orders")
                  "the worker calls the web application's route"

              Expect.contains
                  (into containers "external:filesystem")
                  (web, "writes files")
                  "the web application writes through the libraries it references, as its routes are traced to"

              Expect.isFalse
                  (into containers "external:filesystem" |> List.contains (C4.containerId "Shop.Worker", "writes files"))
                  "the worker reaches no library code that writes"
          }

          test "components: a container's units and its libraries', what each one handles and reaches" {
              let m = ExitPointTests.shopModel.Force()
              let web = C4.containerId "Shop.Web"

              let components =
                  m.Architecture.Elements
                  |> List.filter (fun e -> e.Kind = C4Kind.Component && e.Parent = Some web)
                  |> List.map (fun e -> e.Name, e.Tags)
                  |> Map.ofList

              Expect.equal components["Shop.Web.Handlers"] [ "handles requests"; "reaches out" ] "the handlers"
              Expect.equal components["Shop.Data.Repository"] [ "library Shop.Data"; "reaches out" ] "a library's unit is a component of the container"
              Expect.isTrue (components.ContainsKey "Shop.Storage") "the C# library too"

              let v = view m "components:Shop.Web"
              let handlers = C4.componentId web "Shop.Web:Shop.Web.Handlers"

              Expect.containsAll
                  (into v handlers)
                  [ "person:user", "sends HTTP requests to"
                    C4.containerId "Shop.Worker", "sends HTTP requests to /api/orders"
                    C4.componentId web "Shop.Web:Shop.Web.Program", "uses" ]
                  "who calls the handlers: the user, the worker (another container, shown whole), the program"

              Expect.isFalse
                  (v.Elements |> List.contains "external:processes")
                  "only the outside elements its components are related to"
          }

          test "system context of the C# fixtures: client libraries name the systems they reach" {
              let m = CSharpTests.csharpModel.Force()
              let systems = named m C4Kind.ExternalSystem

              Expect.containsAll
                  systems
                  [ "StackExchange.Redis"; "MongoDB.Driver"; "Amazon.S3"; "Message broker"; "File system" ]
                  "a database, a document store, a storage service, a broker that delivers messages, files"
          } ]
