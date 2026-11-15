module Floorplan.Tests.NoiseAndTraceTests

open System
open System.IO
open Expecto
open Floorplan.Model
open Floorplan.Cli

let private exitIds (model: AnalysisModel) (pred: ExitPoint -> bool) =
    model.ExitPoints |> List.filter pred |> List.map (fun x -> x.Id) |> set

let private traceOf (model: AnalysisModel) (handlerSuffix: string) =
    let entry = model.EntryPoints |> List.find (fun e -> e.HandlerMember.EndsWith handlerSuffix)
    let trace = model.Traces |> List.find (fun t -> t.EntryPointId = entry.Id)
    trace.ExitPointIds |> List.map (fun id -> model.ExitPoints |> List.find (fun x -> x.Id = id))

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Traces and noise control"
        [ test "every entry point has a trace; handlers reach their effects across projects" {
              let m = ExitPointTests.shopModel.Force()
              Expect.equal m.Traces.Length m.EntryPoints.Length "one trace per entry point"

              let price = traceOf m "::price"
              Expect.exists price (fun x -> x.Category = EffectCategory.HttpOut) "price calls the price service"

              let create = traceOf m "::createOrder"
              Expect.exists create (fun x -> x.Category = EffectCategory.FilesystemWrite && x.Unit = "Shop.Storage:*") "create writes through the C# library"

              let list = traceOf m "::listOrders"
              Expect.exists list (fun x -> x.Category = EffectCategory.FilesystemRead) "list reads the orders file"
              Expect.isFalse (list |> List.exists (fun x -> x.Category = EffectCategory.FilesystemWrite)) "list does not write"
          }

          test "direct calls are high confidence; wrapper libraries are not low" {
              let m = ExitPointTests.effectsModel.Force()

              for x in m.ExitPoints |> List.filter (fun x -> x.ViaAssembly.IsNone) do
                  Expect.equal x.Confidence Confidence.High $"{x.Member} direct"

              let dapper = m.ExitPoints |> List.find (fun x -> x.Member = "Effects.Wrapped::queryNames" && x.Category = EffectCategory.Database)
              Expect.notEqual dapper.Confidence Confidence.Low "Dapper is a short path"
          }

          test "presentation: environment and nondeterminism hidden by default, --show reveals them" {
              let solution = Fixtures.effects.Force()
              let defaults = ExitPointTests.effectsModel.Force()
              Expect.containsAll defaults.Presentation.HiddenCategories [ EffectCategory.Environment; EffectCategory.Nondeterminism ] "hidden"
              Expect.equal defaults.Presentation.HiddenConfidence [ Confidence.Low ] "low confidence hidden"

              let shown = Pipeline.buildModel ignore Caching.Store.Disabled [] [ EffectCategory.Environment ] solution
              Expect.equal shown.Presentation.HiddenCategories [ EffectCategory.Nondeterminism ] "--show environment"
          }

          test "a warm summary cache gives the same exit points" {
              let dir = Path.Combine(Path.GetTempPath(), "floorplan-cache-" + string (Guid.NewGuid()))
              let store = Caching.Store(Some dir)
              let solution = Fixtures.effects.Force()

              let shape (m: AnalysisModel) =
                  m.ExitPoints |> List.map (fun x -> x.Category, x.Member, x.Primitive, x.ViaAssembly, x.Location)

              let cold = Pipeline.buildModel ignore store [] [] solution
              Expect.isTrue (Directory.Exists(Path.Combine(dir, "summaries"))) "summaries written"
              Expect.isTrue (Directory.Exists(Path.Combine(dir, "structure"))) "structure written"

              let warm = Pipeline.buildModel ignore store [] [] solution
              Expect.equal (shape warm) (shape cold) "same results"
              Expect.equal warm.Units cold.Units "same structure"
              try Directory.Delete(dir, true) with _ -> ()
          } ]
