module Floorplan.Tests.C4Tests

open Expecto
open Floorplan.Model
open Floorplan.Architecture

let private render (model: AnalysisModel) =
    (C4.describe model.Architecture |> String.concat "\n") + "\n"

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
                  "the host of an absolute URL, files and processes; not the environment, hidden by default"

              let into (target: string) =
                  m.Architecture.Relationships |> List.filter (fun r -> r.To = target) |> List.map (fun r -> r.From, r.Description)

              Expect.equal (into "external:http:prices.example.org") [ C4.SystemId, "sends HTTP requests" ] "the system calls the price service"
              Expect.equal (into C4.SystemId) [ "person:user", "sends HTTP requests to" ] "the user calls the system"

              Expect.equal
                  (m.Architecture.Views |> List.map (fun v -> v.Id, v.Level))
                  [ "context", C4Level.Context ]
                  "one view per level so far"
          }

          test "system context of the C# fixtures: client libraries name the systems they reach" {
              let m = CSharpTests.csharpModel.Force()
              let systems = named m C4Kind.ExternalSystem

              Expect.containsAll
                  systems
                  [ "StackExchange.Redis"; "MongoDB.Driver"; "Amazon.S3"; "Message broker"; "File system" ]
                  "a database, a document store, a storage service, a broker that delivers messages, files"
          } ]
