module Floorplan.Tests.StructureTests

open Expecto
open Floorplan.Model
open Floorplan.Structure
open Floorplan.Structure.FSharp
open Floorplan.Structure.CSharp

let shopStructure =
    lazy
        (let solution = Fixtures.shop.Force()
         StructureAnalysis.analyze [ FSharpStructureAnalyzer.create Floorplan.Model.Caching.Store.Disabled ] solution.Projects |> fst)

let private bothLanguages (solution: Floorplan.ProjectLoading.LoadedSolution) =
    StructureAnalysis.analyze
        [ FSharpStructureAnalyzer.create Floorplan.Model.Caching.Store.Disabled
          CSharpStructureAnalyzer.create Floorplan.Model.Caching.Store.Disabled ]
        solution.Projects

let mixedStructure = lazy (bothLanguages (Fixtures.mixed.Force()))

let private hasEdge (from: string) (target: string) (result: StructureResult) =
    result.Dependencies |> List.exists (fun d -> d.FromUnit = from && d.ToUnit = target)

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Structure analysis"
        [ test "finds modules and nested modules, keyed by project" {
              let result = shopStructure.Force()
              let ids = result.Units |> List.map (fun u -> u.Id) |> set

              for id in
                  [ "Shop.Core:Shop.Core.Domain"
                    "Shop.Core:Shop.Core.Orders"
                    "Shop.Core:Shop.Core.Orders.Validation"
                    "Shop.Web:Shop.Web.Handlers"
                    "Shop.Web:Shop.Web.Program" ] do
                  Expect.contains ids id id

              let validation = result.Units |> List.find (fun u -> u.Id = "Shop.Core:Shop.Core.Orders.Validation")
              Expect.equal validation.Parent (Some "Shop.Core:Shop.Core.Orders") "nested module parent"
              Expect.equal validation.DisplayName "Validation" "display name"
          }

          test "keeps two modules with the same name in different projects apart" {
              let result = shopStructure.Force()
              let utils = result.Units |> List.filter (fun u -> u.Name = "Util") |> List.map (fun u -> u.Id) |> List.sort
              Expect.equal utils [ "Shop.Core:Util"; "Shop.Data:Util" ] "two Util modules"
              Expect.isTrue (hasEdge "Shop.Core:Shop.Core.Orders" "Shop.Core:Util" result) "Orders uses Core's Util"
              Expect.isTrue (hasEdge "Shop.Data:Shop.Data.Repository" "Shop.Data:Util" result) "Repository uses Data's Util"
              Expect.isFalse (hasEdge "Shop.Data:Shop.Data.Repository" "Shop.Core:Util" result) "not Core's Util"
          }

          test "finds edges within and across projects" {
              let result = shopStructure.Force()
              Expect.isTrue (hasEdge "Shop.Core:Shop.Core.Orders" "Shop.Core:Shop.Core.Orders.Validation" result) "nested"
              Expect.isTrue (hasEdge "Shop.Core:Shop.Core.Orders" "Shop.Core:Shop.Core.Domain" result) "type use"

              let cross =
                  result.Dependencies
                  |> List.find (fun d -> d.FromUnit = "Shop.Web:Shop.Web.Handlers" && d.ToUnit = "Shop.Data:Shop.Data.Repository")

              Expect.isTrue cross.CrossProject "cross-project flag"
              Expect.isGreaterThanOrEqual cross.Weight 2 "two uses"
              Expect.isNonEmpty cross.SampleLocations "sample locations"
          }

          test "a C# project is one limited-support unit that F# code depends on" {
              let result = shopStructure.Force()
              let storage = result.Units |> List.find (fun u -> u.ProjectId = "Shop.Storage")
              Expect.equal storage.Kind UnitKind.ProjectUnit "project unit"
              Expect.isTrue (hasEdge "Shop.Data:Shop.Data.Repository" storage.Id result) "F# to C# edge"

              Expect.exists result.Diagnostics (fun d -> d.Code = "limited-support" && d.ProjectId = Some "Shop.Storage") "diagnostic"
          }

          test "no type-check errors on the fixture" {
              let result = shopStructure.Force()
              let errors = result.Diagnostics |> List.filter (fun d -> d.Code.StartsWith "structure.")
              Expect.isEmpty errors "clean"
          }

          test "C#: with its analyzer, a C# project has full support and a unit per namespace" {
              let result, index = bothLanguages (Fixtures.shop.Force())
              let storage = Fixtures.project (Fixtures.shop.Force()) "Shop.Storage"
              Expect.equal (SolutionIndex.supportLevel storage index) SupportLevel.Full "full support"

              let units = result.Units |> List.filter (fun u -> u.ProjectId = "Shop.Storage")
              Expect.equal (units |> List.map (fun u -> u.Id, u.Kind)) [ "Shop.Storage:Shop.Storage", UnitKind.Namespace ] "one namespace unit"
              Expect.isTrue (hasEdge "Shop.Data:Shop.Data.Repository" "Shop.Storage:Shop.Storage" result) "F# to C# edge"
              Expect.isFalse (result.Diagnostics |> List.exists (fun d -> d.Code = "limited-support")) "no limited support"
          }

          test "C#: namespaces that declare types are units, types outside any namespace the global one" {
              let result, _ = mixedStructure.Force()
              let units = result.Units |> List.filter (fun u -> u.ProjectId = "Mixed.Billing") |> List.sortBy (fun u -> u.Id)

              Expect.equal
                  (units |> List.map (fun u -> u.Id, u.DisplayName))
                  [ "Mixed.Billing:", CSharpStructureAnalyzer.globalNamespaceLabel
                    "Mixed.Billing:Mixed.Billing", "Billing"
                    "Mixed.Billing:Mixed.Billing.Storage", "Storage" ]
                  "units"

              let storage = units |> List.find (fun u -> u.Name = "Mixed.Billing.Storage")
              Expect.stringEnds storage.Location.Value.File "InvoiceStore.cs" "block-scoped namespace location"
              Expect.isNone (units |> List.find (fun u -> u.Name = "")).Location "the global namespace has no location"

              let tool = result.Units |> List.filter (fun u -> u.ProjectId = "Mixed.Tool") |> List.map (fun u -> u.Id)
              Expect.equal tool [ "Mixed.Tool:" ] "top-level statements are in the global namespace"
          }

          test "C#: edges within a project, a namespace cycle, and edges both ways between C# and F#" {
              let result, _ = mixedStructure.Force()
              Expect.isTrue (hasEdge "Mixed.Billing:Mixed.Billing" "Mixed.Billing:Mixed.Billing.Storage" result) "Billing uses Storage"
              Expect.isTrue (hasEdge "Mixed.Billing:Mixed.Billing.Storage" "Mixed.Billing:Mixed.Billing" result) "and back: a cycle"
              Expect.isTrue (hasEdge "Mixed.Billing:Mixed.Billing.Storage" "Mixed.Billing:" result) "Storage uses a type in no namespace"

              let toFSharp =
                  result.Dependencies |> List.find (fun d -> d.FromUnit = "Mixed.Billing:Mixed.Billing" && d.ToUnit = "Mixed.Core:Mixed.Core.Pricing")

              Expect.isTrue toFSharp.CrossProject "C# to an F# module, across projects"
              Expect.isNonEmpty toFSharp.SampleLocations "sample locations"
              Expect.isTrue (hasEdge "Mixed.App:Mixed.App.Program" "Mixed.Billing:Mixed.Billing" result) "F# to a C# namespace"
              Expect.isTrue (hasEdge "Mixed.Tool:" "Mixed.Billing:Mixed.Billing" result) "top-level statements to a namespace"
          }

          test "C#: no compile errors on the mixed fixture" {
              let result, _ = mixedStructure.Force()
              let errors = result.Diagnostics |> List.filter (fun d -> d.Code.StartsWith "structure.")
              Expect.isEmpty errors "clean"
          } ]
