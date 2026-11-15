/// Fixture solutions under tests/fixtures, loaded once per test run.
module Floorplan.Tests.Fixtures

open System.IO
open Floorplan.ProjectLoading

let fixturesDir = Path.Combine(ArchitectureTests.repoRoot, "tests", "fixtures")

let path (relative: string) = Path.Combine(fixturesDir, relative)

let private loadOrFail options relative =
    match SolutionLoader.load options (Some(path relative)) with
    | Ok solution -> solution
    | Error e -> failwith (SolutionLoader.describe e)

/// The Shop fixture: two applications, two F# libraries, a C# library and a test project.
let shop = lazy (loadOrFail LoadOptions.defaults "Shop/Shop.slnx")

let shopWithTests =
    lazy (loadOrFail { LoadOptions.defaults with IncludeTests = true; Build = false } "Shop/Shop.slnx")

/// The Effects fixture: every effect category, called directly, through wrapper libraries and higher-order code.
let effects = lazy (loadOrFail LoadOptions.defaults "Effects/Effects.slnx")

let project (solution: LoadedSolution) (name: string) =
    solution.Projects |> List.find (fun p -> p.Name = name)
