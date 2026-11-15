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
