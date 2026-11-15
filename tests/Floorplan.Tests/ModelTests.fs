module Floorplan.Tests.ModelTests

open Expecto
open Floorplan.Model

let private sample =
    { AnalysisModel.empty "0.1.0" "2026-11-15T00:00:00Z" { Name = "App"; Path = "App.sln" } with
        Projects =
            [ { Id = "p1"
                Name = "App"
                Path = "App/App.fsproj"
                Language = Language.FSharp
                SupportLevel = SupportLevel.Full
                TargetFramework = "net8.0"
                IsApplication = true
                IsTestProject = false
                CatalogUsed = Some "net8.0"
                CatalogFallback = false
                ProjectReferences = [] } ]
        Units =
            [ { Id = "p1:App.Orders"
                ProjectId = "p1"
                Kind = UnitKind.Module
                Name = "App.Orders"
                DisplayName = "Orders"
                Parent = None
                Location = None } ] }

[<Tests>]
let tests =
    testList
        "Model JSON"
        [ test "round-trips" {
              let json = Json.serialize true sample
              Expect.equal (Json.deserialize json) sample "round trip"
          }

          test "writes unions as kebab-case strings and options as null" {
              let json = Json.serialize false sample
              Expect.stringContains json "\"language\":\"fsharp\"" "language"
              Expect.stringContains json "\"kind\":\"module\"" "unit kind"
              Expect.stringContains json "\"supportLevel\":\"full\"" "support level"
              Expect.stringContains json "\"parent\":null" "none"
              Expect.stringContains json "\"catalogUsed\":\"net8.0\"" "some"
          }

          test "category names match the catalog spelling" {
              Expect.equal (EffectCategory.name EffectCategory.FilesystemRead) "filesystem-read" "name"
              Expect.equal (Json.unionName EffectCategory.FilesystemRead) "filesystem-read" "json name"
              Expect.equal (EffectCategory.tryParse "http-out") (Some EffectCategory.HttpOut) "parse"
          } ]
