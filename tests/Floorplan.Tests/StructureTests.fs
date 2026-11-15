module Floorplan.Tests.StructureTests

open Expecto
open Floorplan.Model
open Floorplan.Structure
open Floorplan.Structure.FSharp

let shopStructure =
    lazy
        (let solution = Fixtures.shop.Force()
         StructureAnalysis.analyze [ FSharpStructureAnalyzer.create () ] solution.Projects |> fst)

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
          } ]
