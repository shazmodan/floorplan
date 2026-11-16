module Floorplan.Tests.ProjectLoadingTests

open System.IO
open Expecto
open Floorplan.Model
open Floorplan.ProjectLoading

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Solution loader"
        [ test "loads every non-test project of the solution" {
              let solution = Fixtures.shop.Force()
              let names = solution.Projects |> List.map (fun p -> p.Name) |> List.sort

              Expect.equal names [ "Shop.Core"; "Shop.Data"; "Shop.Storage"; "Shop.Web"; "Shop.Worker" ] "projects"
              Expect.equal solution.Skipped [ "Shop.Tests" ] "test project skipped"
              Expect.isEmpty solution.Failures "no failures"
          }

          test "includes test projects on request" {
              let solution = Fixtures.shopWithTests.Force()
              let tests = Fixtures.project solution "Shop.Tests"
              Expect.isTrue tests.IsTestProject "flagged as test project"
          }

          test "records language, target framework and application flag" {
              let solution = Fixtures.shop.Force()
              let get = Fixtures.project solution
              Expect.equal (get "Shop.Storage").Language Language.CSharp "C# project"
              Expect.equal (get "Shop.Core").Language Language.FSharp "F# project"
              Expect.equal (get "Shop.Core").TargetFramework "net8.0" "net8 library"
              Expect.equal (get "Shop.Web").TargetFramework "net10.0" "net10 web app"
              Expect.isTrue (get "Shop.Web").IsApplication "web SDK project is an application"
              Expect.isTrue (get "Shop.Worker").IsApplication "exe is an application"
              Expect.isFalse (get "Shop.Core").IsApplication "library is not"
          }

          test "a preferred framework applies to multi-targeting projects only" {
              Fixtures.shop.Force() |> ignore

              // Shop mixes net8.0 and net10.0 projects that target one framework each: all of them stay.
              let shop =
                  match SolutionLoader.load { LoadOptions.defaults with Framework = Some "net8.0"; Build = false } (Some(Fixtures.path "Shop/Shop.slnx")) with
                  | Ok s -> s
                  | Error e -> failwith (SolutionLoader.describe e)

              Expect.isEmpty shop.Failures "no failures"
              Expect.equal (Fixtures.project shop "Shop.Web").TargetFramework "net10.0" "a net10.0-only project keeps net10.0"
              Expect.equal (Fixtures.project shop "Shop.Core").TargetFramework "net8.0" "net8.0 library"

              // A project that targets both takes the one asked for, and the first one listed otherwise.
              let routing = Fixtures.routingNet10.Force()
              let endpoints = Fixtures.project routing "Routing.Endpoints"
              Expect.equal endpoints.TargetFrameworks [ "net8.0"; "net10.0" ] "multi-targeting"
              Expect.equal endpoints.TargetFramework "net10.0" "preferred framework"
              Expect.stringContains endpoints.OutputAssembly "net10.0" "the net10.0 build"
              Expect.equal (Fixtures.project (Fixtures.routing.Force()) "Routing.Endpoints").TargetFramework "net8.0" "first by default"
          }

          test "records source files in compile order and project references" {
              let solution = Fixtures.shop.Force()
              let core = Fixtures.project solution "Shop.Core"
              let data = Fixtures.project solution "Shop.Data"

              let fsFiles =
                  core.SourceFiles
                  |> List.map Path.GetFileName
                  |> List.filter (fun f -> f.EndsWith ".fs" && not (f.Contains "AssemblyAttributes" || f.Contains "AssemblyInfo"))

              Expect.equal fsFiles [ "Domain.fs"; "Util.fs"; "Orders.fs" ] "compile order"
              Expect.equal (List.sort data.ProjectReferences) [ "Shop.Core"; "Shop.Storage" ] "references"
              Expect.isTrue (File.Exists core.OutputAssembly) "built assembly"
              Expect.isSome core.Pdb "pdb"
              Expect.isNonEmpty core.ReferencePaths "reference paths"
          }

          test "--include keeps a project and what it references" {
              let options =
                  { LoadOptions.defaults with
                      Include = [ "Shop.Worker" ]
                      Build = false }

              match SolutionLoader.load options (Some(Fixtures.path "Shop/Shop.slnx")) with
              | Ok solution ->
                  let names = solution.Projects |> List.map (fun p -> p.Name) |> List.sort
                  Expect.equal names [ "Shop.Core"; "Shop.Worker" ] "closure"
              | Error e -> failtest (SolutionLoader.describe e)
          }

          test "a directory resolves to its solution" {
              match Discovery.resolve (Some(Fixtures.path "Shop")) with
              | Ok path -> Expect.equal (Path.GetFileName path) "Shop.slnx" "solution"
              | Error e -> failtest (SolutionLoader.describe e)
          } ]
