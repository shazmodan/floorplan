module Floorplan.Tests.CliTests

open System
open System.IO
open Argu
open Expecto
open Floorplan.Model
open Floorplan.Cli
open Floorplan.Cli.Options

let private parse (argv: string list) =
    let parser = ArgumentParser.Create<Command>(programName = "floorplan")
    let results = parser.ParseCommandLine(List.toArray argv)
    toOptions (results.GetResult Analyze)

[<Tests>]
let tests =
    testSequenced
    <| testList
        "CLI"
        [ test "parses the analyze options of plan section 7.3" {
              match
                  parse
                      [ "analyze"; "App.sln"; "-o"; "out"; "--include"; "A,B"; "--exclude"; "C"; "--include-tests"
                        "-f"; "net8.0"; "-c"; "Release"; "--format"; "json"; "--single-file"; "--catalog"; "x.json"
                        "--show"; "environment"; "--no-build" ]
              with
              | Error e -> failtest e
              | Ok o ->
                  Expect.equal o.Path (Some "App.sln") "path"
                  Expect.equal o.Output "out" "output"
                  Expect.equal o.Include [ "A"; "B" ] "include"
                  Expect.equal o.Exclude [ "C" ] "exclude"
                  Expect.isTrue o.IncludeTests "tests"
                  Expect.equal o.Framework (Some "net8.0") "framework"
                  Expect.equal o.Configuration "Release" "configuration"
                  Expect.equal o.Format Json "format"
                  Expect.isTrue o.SingleFile "single file"
                  Expect.equal o.Catalogs [ "x.json" ] "catalog"
                  Expect.equal o.Show [ EffectCategory.Environment ] "show"
                  Expect.isFalse o.Build "no build"
          }

          test "defaults" {
              match parse [ "analyze" ] with
              | Error e -> failtest e
              | Ok o ->
                  Expect.equal o.Path None "path"
                  Expect.equal o.Output "floorplan-out" "output"
                  Expect.equal o.Configuration "Debug" "configuration"
                  Expect.equal o.Format Site "format"
                  Expect.isTrue o.Build "builds"
          }

          test "rejects unknown categories" {
              match parse [ "analyze"; "--show"; "bogus" ] with
              | Error e -> Expect.stringContains e "bogus" "names the bad category"
              | Ok _ -> failtest "accepted"
          }

          test "analyzes the Shop fixture end to end" {
              let out = Path.Combine(Path.GetTempPath(), "floorplan-cli-" + string (Guid.NewGuid()))

              let options =
                  { Options.defaults with
                      Path = Some(Fixtures.path "Shop/Shop.slnx")
                      Output = out
                      Build = false
                      Cache = false
                      Quiet = true }

              match Pipeline.run ignore options with
              | Error e -> failtest e
              | Ok outcome ->
                  Expect.isFalse outcome.HadFailures "no failures"
                  Expect.isTrue (File.Exists outcome.Written) "index.html written"
                  Expect.equal outcome.Model.Projects.Length 5 "projects"
                  Expect.isNonEmpty outcome.Model.Layout.UnitGraph.Nodes "layout"
                  Expect.equal outcome.Model.SchemaVersion AnalysisModel.CurrentSchemaVersion "schema version"
          }

          test "prints a failed build once, not with every project" {
              let root = Path.Combine(Path.GetTempPath(), "floorplan-broken-" + string (Guid.NewGuid()))

              let write (relative: string) (text: string) =
                  let file = Path.Combine(root, relative)
                  Directory.CreateDirectory(Path.GetDirectoryName file) |> ignore
                  File.WriteAllText(file, text)

              let project (references: string) =
                  $"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>{references}</ItemGroup></Project>"""

              // The repo's SDK, as for the fixtures: MSBuild is loaded once per process, and a newer SDK installed
              // on the machine (the Ubuntu runner has 11) would otherwise be picked outside the repo.
              write "global.json" (File.ReadAllText(Path.Combine(ArchitectureTests.repoRoot, "global.json")))
              write "Broken.slnx""""<Solution><Project Path="Lib/Lib.csproj" /><Project Path="App/App.csproj" /></Solution>"""
              write "Lib/Lib.csproj" (project "")
              write "Lib/Lib.cs" "public class Lib { int x = \"not a number\"; }"
              write "App/App.csproj" (project """<ProjectReference Include="../Lib/Lib.csproj" />""")
              write "App/App.cs" "public class App { }"

              let options =
                  { Options.defaults with
                      Path = Some(Path.Combine(root, "Broken.slnx"))
                      Output = Path.Combine(root, "out")
                      Cache = false
                      Quiet = true }

              try
                  match Pipeline.run ignore options with
                  | Ok _ -> failtest "analyzed a solution that does not build"
                  | Error e ->
                      let count (text: string) = e.Split(text).Length - 1
                      Expect.equal (count "The build failed:") 1 $"the build log is printed once, in:\n{e}"
                      Expect.equal (count "The project did not build.") 2 $"each project is listed, in:\n{e}"
                      Expect.stringContains e "CS0029" "the compiler error is shown"
              finally
                  try Directory.Delete(root, true) with _ -> ()
          } ]
