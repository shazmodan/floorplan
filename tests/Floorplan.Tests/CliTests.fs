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
          } ]
