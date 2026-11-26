module Floorplan.Tests.LayoutTests

open Expecto
open Floorplan.Model
open Floorplan.Layout

let private node id = Layered.node id 80.0 30.0

let private edge a b = Layered.edge a b

let private overlaps (a: NodeLayout) (b: NodeLayout) =
    a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height

let private noOverlaps (nodes: NodeLayout list) =
    [ for i, a in List.indexed nodes do
          for b in List.skip (i + 1) nodes do
              if overlaps a b then
                  yield a.Id, b.Id ]

let private project id references =
    { Id = id
      Name = id
      Path = id
      Language = Language.FSharp
      SupportLevel = SupportLevel.Full
      TargetFramework = "net8.0"
      IsApplication = false
      IsTestProject = false
      CatalogUsed = None
      CatalogFallback = false
      ProjectReferences = references }

let private unit projectId name =
    { Id = projectId + ":" + name
      ProjectId = projectId
      Kind = UnitKind.Module
      Name = name
      DisplayName = name
      Parent = None
      Location = None
      Types = []
      Aliases = [] }

let private uses (a: CodeUnit) (b: CodeUnit) =
    { FromUnit = a.Id
      ToUnit = b.Id
      Weight = 1
      CrossProject = a.ProjectId <> b.ProjectId
      SampleLocations = [] }

/// Two crossing-free routes: a chain App -> Orders -> Domain plus App -> Domain, and an unused module per project.
let private sample () =
    let app = unit "Web" "App"
    let orders = unit "Web" "Orders"
    let domain = unit "Core" "Domain"
    let unused = [ for i in 1..12 -> unit "Web" $"Plugin{i}" ] @ [ unit "Core" "Reflected"; unit "Tools" "Script" ]

    let projects = [ project "Web" [ "Core" ]; project "Core" []; project "Tools" [] ]
    let units = [ app; orders; domain ] @ unused
    let dependencies = [ uses app orders; uses orders domain; uses app domain ]
    SolutionLayout.compute projects units dependencies, unused

let private entry (handler: CodeUnit) (route: string) =
    { Id = route
      ApplicationId = handler.ProjectId
      HandlerUnit = handler.Id
      HandlerMember = handler.Name + "::handle"
      Location = None
      Category = EffectCategory.HttpIn
      RegisteredThrough = None
      Predicates = []
      HttpMethod = Some "GET"
      Route = Some route
      Completeness = Completeness.Complete
      Forms = []
      Before = []
      Evidence = { Steps = [] } }

let private exit (owner: CodeUnit) (category: EffectCategory) (payload: string) =
    { Id = owner.Id + payload
      Category = category
      Unit = owner.Id
      Member = owner.Name + "::run"
      Location = None
      RuleId = "rule"
      Primitive = "System.IO.File::WriteAllText"
      Payload = Some { Role = "path"; Kind = PayloadKind.Literal; Value = Some payload }
      ViaAssembly = None
      Context = None
      Connection = None
      Confidence = Confidence.High
      Evidence = { Steps = [] } }

[<Tests>]
let tests =
    testList
        "Layout"
        [ test "ranks by longest path; dependencies sit below dependents" {
              let g =
                  Layered.layout
                      Layered.defaults
                      [ node "app"; node "orders"; node "domain" ]
                      [ edge "app" "orders"; edge "orders" "domain"; edge "app" "domain" ]

              let layer id =
                  (g.Nodes |> List.find (fun n -> n.Id = id)).Layer

              Expect.equal (layer "app") 0 "top"
              Expect.equal (layer "orders") 1 "middle"
              Expect.equal (layer "domain") 2 "longest path, not shortest"
              Expect.isEmpty (noOverlaps g.Nodes) "no overlaps"
              Expect.equal g.Edges.Length 3 "edges routed"
          }

          test "collapses a cycle into one node" {
              let g =
                  Layered.layout
                      Layered.defaults
                      [ node "a"; node "b"; node "c"; node "d" ]
                      [ edge "a" "b"; edge "b" "c"; edge "c" "b"; edge "c" "d" ]

              let group = g.Nodes |> List.find (fun n -> not n.Members.IsEmpty)
              Expect.equal group.Members [ "b"; "c" ] "b and c collapsed"
              Expect.equal g.Nodes.Length 3 "three nodes drawn"
              Expect.isEmpty (noOverlaps g.Nodes) "no overlaps"
          }

          test "a long edge is routed through the gap of the layers it crosses, not through their nodes" {
              // a -> b -> c -> d, and a -> d across two layers, with other nodes on those layers.
              let g =
                  Layered.layout
                      Layered.defaults
                      [ node "a"; node "b"; node "c"; node "d"; node "x"; node "y" ]
                      [ edge "a" "b"; edge "b" "c"; edge "c" "d"; edge "a" "d"; edge "a" "x"; edge "x" "y"; edge "y" "d" ]

              let long = g.Edges |> List.find (fun e -> e.From = "a" && e.To = "d")
              Expect.isGreaterThan long.Points.Length 2 "bends at the layers it crosses"

              // No point of the long edge lies inside a node other than its ends.
              for p in long.Points do
                  for n in g.Nodes |> List.filter (fun n -> n.Id <> "a" && n.Id <> "d") do
                      Expect.isFalse (p.X > n.X && p.X < n.X + n.Width && p.Y > n.Y && p.Y < n.Y + n.Height) $"clear of {n.Id}"

              Expect.isEmpty (noOverlaps g.Nodes) "no overlaps"
          }

          test "a node sits over what it uses, not packed to the left" {
              // One parent over a single child, next to a wide layer: the child is centred under its parent.
              let g =
                  Layered.layout
                      Layered.defaults
                      [ node "top"; node "child"; node "w1"; node "w2"; node "w3"; node "w4" ]
                      [ edge "top" "child"; edge "w1" "w2"; edge "w1" "w3"; edge "w1" "w4" ]

              let centre id =
                  let n = g.Nodes |> List.find (fun n -> n.Id = id)
                  n.X + n.Width / 2.0

              Expect.floatClose Accuracy.medium (centre "child") (centre "top") "child under its parent"
              Expect.isEmpty (noOverlaps g.Nodes) "no overlaps"
          }

          test "unconnected units are set out below the graph, by project, in rows" {
              let layout, unused = sample ()
              let graphBottom = layout.ProjectBoxes |> List.map (fun b -> b.Y + b.Height) |> List.max

              Expect.equal (layout.ProjectBoxes |> List.map (fun b -> b.Id) |> List.sort) [ "Core"; "Web" ] "graph boxes"
              Expect.equal (layout.DetachedBoxes |> List.map (fun b -> b.Id)) [ "Web"; "Core"; "Tools" ] "a box per project, in project order"

              for b in layout.DetachedBoxes do
                  Expect.isGreaterThan b.Y graphBottom $"{b.Id} below the graph"

              for u in unused do
                  let n = layout.UnitGraph.Nodes |> List.find (fun n -> n.Id = u.Id)
                  let box = layout.DetachedBoxes |> List.find (fun b -> b.Id = u.ProjectId)

                  Expect.isTrue
                      (n.X >= box.X && n.Y >= box.Y && n.X + n.Width <= box.X + box.Width + 0.01 && n.Y + n.Height <= box.Y + box.Height + 0.01)
                      $"{u.Id} inside its box"

              // Twelve unused modules do not make the drawing twelve modules wide.
              let web = layout.DetachedBoxes |> List.find (fun b -> b.Id = "Web")
              Expect.isLessThanOrEqual web.Width (max 960.0 layout.UnitGraph.Width) "rows wrap"
              Expect.isEmpty (noOverlaps (layout.ProjectBoxes @ layout.DetachedBoxes)) "boxes do not overlap"
              Expect.isEmpty (noOverlaps layout.UnitGraph.Nodes) "nodes do not overlap"
          }

          test "dependencies between projects are drawn once per pair of projects" {
              let layout, _ = sample ()
              Expect.equal (layout.ProjectEdges |> List.map (fun e -> e.From, e.To)) [ "Web", "Core" ] "one edge, Web to Core"
              Expect.isFalse (layout.UnitGraph.Edges |> List.exists (fun e -> e.From.StartsWith "Web:" && e.To.StartsWith "Core:")) "no unit-level cross edges"
          }

          test "the flow graph: entry cards above their handlers, the units between, exit cards below their units" {
              let api = unit "Web" "Api"
              let orders = unit "Web" "Orders"
              let store = unit "Core" "Store"
              let clock = unit "Core" "Clock"
              let unrelated = unit "Core" "Reports"
              let units = [ api; orders; store; clock; unrelated ]
              // Reports uses Store, but handles no request and has no exit points of its own: it is left out.
              let dependencies = [ uses api orders; uses orders store; uses unrelated store; uses orders clock ]
              let entries = [ entry api "/orders"; entry api "/orders/{id}" ]

              let exits =
                  [ exit store EffectCategory.Database "SELECT * FROM orders"
                    exit store EffectCategory.Database "SELECT * FROM orders"
                    exit store EffectCategory.FilesystemWrite "audit.log"
                    exit clock EffectCategory.Nondeterminism "now" ]

              let presentation = (AnalysisModel.empty "t" "t" { Name = "s"; Path = "p" }).Presentation
              let g = SolutionLayout.flow units dependencies entries exits presentation
              let ids = g.Nodes |> List.map (fun n -> n.Id) |> set

              Expect.equal
                  ids
                  (set
                      [ "entries:" + api.Id
                        "unit:" + api.Id
                        "unit:" + orders.Id
                        "unit:" + store.Id
                        $"exits:{store.Id}|database"
                        $"exits:{store.Id}|filesystem-write" ])
                  "handlers, the units between, units with exits and their cards; not Clock (its only exit is hidden by default) nor Reports"

              let at id = g.Nodes |> List.find (fun n -> n.Id = id)
              Expect.isLessThan (at ("entries:" + api.Id)).Y (at ("unit:" + api.Id)).Y "entry card above its handler"
              Expect.isGreaterThan (at $"exits:{store.Id}|database").Y (at ("unit:" + store.Id)).Y "exit card below its unit"
              Expect.equal (at ("entries:" + api.Id)).Height (SolutionLayout.cardHeight 2) "one line per route"
              Expect.equal (at $"exits:{store.Id}|database").Height (SolutionLayout.cardHeight 1) "one line per distinct payload"
              Expect.isEmpty (noOverlaps g.Nodes) "no overlaps"
              Expect.equal (SolutionLayout.cardHeight 50) (SolutionLayout.cardHeight SolutionLayout.cardLines) "long cards are capped"
          }

          test "is deterministic" {
              let build () =
                  Layered.layout
                      Layered.defaults
                      [ for i in 1..12 -> node $"n{i}" ]
                      [ for i in 1..11 -> edge $"n{i}" $"n{(i * 7) % 12 + 1}" ]

              Expect.equal (build ()) (build ()) "same input, same layout"
          }

          test "places every unit inside its project's box" {
              let structure = StructureTests.shopStructure.Force()
              let solution = Fixtures.shop.Force()

              let projects =
                  solution.Projects
                  |> List.map (fun p ->
                      { Id = p.Id
                        Name = p.Name
                        Path = p.Path
                        Language = p.Language
                        SupportLevel = SupportLevel.Full
                        TargetFramework = p.TargetFramework
                        IsApplication = p.IsApplication
                        IsTestProject = p.IsTestProject
                        CatalogUsed = None
                        CatalogFallback = false
                        ProjectReferences = p.ProjectReferences })

              let layout = SolutionLayout.compute projects structure.Units structure.Dependencies
              Expect.equal layout.ProjectBoxes.Length projects.Length "one box per project"
              Expect.isEmpty (noOverlaps layout.ProjectBoxes) "boxes do not overlap"

              for u in structure.Units do
                  let n =
                      layout.UnitGraph.Nodes
                      |> List.find (fun n -> n.Id = u.Id || List.contains u.Id n.Members)

                  let box = layout.ProjectBoxes |> List.find (fun b -> b.Id = u.ProjectId)

                  Expect.isTrue
                      (n.X >= box.X && n.Y >= box.Y && n.X + n.Width <= box.X + box.Width + 0.01
                       && n.Y + n.Height <= box.Y + box.Height + 0.01)
                      $"{u.Id} inside {box.Id}"

              let web = layout.ProjectGraph.Nodes |> List.find (fun n -> n.Id = "Shop.Web")
              let core = layout.ProjectGraph.Nodes |> List.find (fun n -> n.Id = "Shop.Core")
              Expect.isLessThan web.Layer core.Layer "web above core"
          } ]
