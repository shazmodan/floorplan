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
