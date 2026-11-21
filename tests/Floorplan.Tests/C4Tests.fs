module Floorplan.Tests.C4Tests

open System.Text.RegularExpressions
open Expecto
open Floorplan.Model
open Floorplan.Architecture
open Floorplan.Rendering

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

          test "code: a component's types, the compiler's own nested types standing for the type they are in" {
              let m = ExitPointTests.shopModel.Force()
              let handlers = C4.componentId (C4.containerId "Shop.Web") "Shop.Web:Shop.Web.Handlers"

              let code =
                  m.Architecture.Elements
                  |> List.filter (fun e -> e.Parent = Some handlers)
                  |> List.map (fun e -> e.Name, e.Technology, e.Tags)

              Expect.equal
                  code
                  [ "Handlers", Some "F# module", [ "handles requests"; "reaches out" ]
                    "FileNotifier", Some "type", [ "reaches out" ] ]
                  "the module, and the type nested in it whose method writes the notices"

              Expect.equal (C4.codeTypeOf "CSharp.Clients.Caching.Cache/<Remember>d__2::MoveNext") "CSharp.Clients.Caching.Cache" "an async state machine"
              Expect.equal (C4.codeTypeOf "Shop.Web.Handlers/price@18::Invoke") "Shop.Web.Handlers" "an F# closure"
              Expect.equal (C4.codeTypeOf "Program/<>c::<<Main>$>b__0_1") "Program" "a lambda's class"
              Expect.equal (C4.codeTypeOf "Shop.Web.Handlers/FileNotifier::Notify") "Shop.Web.Handlers/FileNotifier" "a nested type of its own"

              let v = view m ("code:" + handlers.Substring 10)

              Expect.containsAll
                  (into v (C4.codeId handlers "Shop.Web.Handlers"))
                  [ "person:user", "sends HTTP requests to"
                    C4.componentId (C4.containerId "Shop.Worker") "Shop.Worker:Shop.Worker.Program", "sends HTTP requests to /api/orders" ]
                  "callers reach the handler's type; another container's component is shown whole"

              Expect.isFalse
                  (m.Architecture.Elements |> List.exists (fun e -> e.Kind = C4Kind.Component && e.Name = ""))
                  "a global namespace has its label"
          }

          test "layout: every element of every view is a box of its own, no two overlapping, every relationship drawn" {
              for m in [ ExitPointTests.shopModel.Force(); CSharpTests.csharpModel.Force() ] do
                  for v in m.Architecture.Views do
                      let nodes = v.Layout.Nodes

                      Expect.equal (nodes |> List.map (fun n -> n.Id) |> List.sort) (List.sort v.Elements) $"{v.Id}: a box per element"
                      Expect.isTrue (nodes |> List.forall (fun n -> n.Members.IsEmpty)) $"{v.Id}: no cycle collapsed"

                      for a in nodes do
                          for b in nodes do
                              if a.Id < b.Id then
                                  let apart = a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y
                                  Expect.isTrue apart $"{v.Id}: {a.Id} and {b.Id} overlap"

                      Expect.equal
                          (v.Layout.Edges |> List.map (fun e -> e.From, e.To) |> List.sort)
                          (v.Relationships |> List.map (fun r -> r.From, r.To) |> List.distinct |> List.sort)
                          $"{v.Id}: a line per pair of related elements"
          }

          test "golden: Shop as Structurizr DSL" { Golden.check "shop.workspace.dsl" (StructurizrDsl.write (ExitPointTests.shopModel.Force())) }

          test "golden: C# fixtures as Structurizr DSL" {
              Golden.check "csharp.workspace.dsl" (StructurizrDsl.write (CSharpTests.csharpModel.Force()))
          }

          test "Structurizr DSL: safe identifiers, each declared once, names unique among siblings, no repeated relationship" {
              for m in [ ExitPointTests.shopModel.Force(); CSharpTests.csharpModel.Force() ] do
                  let lines = (StructurizrDsl.write m).Split('\n') |> Array.map (fun l -> l.Trim())
                  let declaration = Regex(@"^([A-Za-z_][A-Za-z0-9_]*) = (person|softwareSystem|container|component) ""([^""]*)""")
                  let relationship = Regex(@"^([A-Za-z0-9_]+) -> ([A-Za-z0-9_]+) ""([^""]*)""")

                  let declared = lines |> Array.choose (fun l -> let d = declaration.Match l in if d.Success then Some d else None)
                  let ids = declared |> Array.map (fun d -> d.Groups[1].Value)
                  Expect.equal (Array.distinct ids).Length ids.Length "each identifier declared once"

                  Expect.equal
                      ids.Length
                      (m.Architecture.Elements |> List.filter (fun e -> e.Kind <> C4Kind.Code) |> List.length)
                      "every element but the code level"

                  let known = Set.ofArray ids
                  let rels = lines |> Array.choose (fun l -> let r = relationship.Match l in if r.Success then Some r else None)
                  Expect.isNonEmpty rels "relationships"

                  for r in rels do
                      Expect.isTrue (known.Contains r.Groups[1].Value && known.Contains r.Groups[2].Value) $"declared ends: {r.Value}"

                  let triples = rels |> Array.map (fun r -> r.Groups[1].Value, r.Groups[2].Value, r.Groups[3].Value)
                  Expect.equal (Array.distinct triples).Length triples.Length "no relationship repeated"

                  for v in [ "systemContext"; "container"; "component" ] do
                      Expect.isTrue (lines |> Array.exists (fun l -> l.StartsWith(v + " "))) $"a {v} view"

              let shop = StructurizrDsl.write (ExitPointTests.shopModel.Force())
              Expect.stringContains shop "component \"Util (Shop.Data)\"" "a library's unit named like another's is qualified by its project"
              Expect.stringContains shop "component_Shop_Worker_Shop_Worker_Shop_Worker_Program -> component_Shop_Web_Shop_Web_Shop_Web_Handlers \"sends HTTP requests to /api/orders\" \"HTTP\"" "code-level relationships are written at their components"
          }

          test "system context of the C# fixtures: client libraries name the systems they reach" {
              let m = CSharpTests.csharpModel.Force()
              let systems = named m C4Kind.ExternalSystem

              Expect.containsAll
                  systems
                  [ "StackExchange.Redis"; "MongoDB.Driver"; "Amazon.S3"; "RabbitMQ.Client"; "File system" ]
                  "a database, a document store, a storage service, a broker that delivers messages, files"
          }

          test "who sends a message: the system its library reaches, one element with what is sent through it" {
              let m = CSharpTests.csharpModel.Force()
              let containers = view m "containers"
              let messaging = C4.containerId "CSharp.Messaging"

              Expect.contains
                  (into containers messaging)
                  ("external:library:RabbitMQ.Client", "delivers messages to")
                  "RabbitMQ delivers the messages its consumers receive"

              Expect.isTrue
                  (containers.Relationships |> List.exists (fun r -> r.From = messaging && r.To = "external:library:RabbitMQ.Client"))
                  "and the application sends to the same element"

              Expect.isFalse (named m C4Kind.ExternalSystem |> List.contains "Message broker") "every consumer's library is known"

              Expect.isEmpty
                  (into containers (C4.containerId "CSharp.Functions") |> List.filter (fun (from, _) -> from = messaging))
                  "a queue named orders on RabbitMQ is not the Functions host's queue of that name"

              let routing = EntryPointTests.routingModel.Force()

              Expect.contains
                  (into (view routing "containers") (C4.containerId "Routing.Socket"))
                  ("external:network", "sends messages to")
                  "messages on the platform's own sockets come from the network, not a broker"
          } ]
