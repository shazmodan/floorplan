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

/// The Mixed fixture: F# and C# projects that call each other, C# namespaces in a cycle, a C# global namespace.
let mixed = lazy (loadOrFail LoadOptions.defaults "Mixed/Mixed.slnx")

/// The Effects fixture: every effect category, called directly, through wrapper libraries and higher-order code.
let effects = lazy (loadOrFail LoadOptions.defaults "Effects/Effects.slnx")

/// The Routing fixture: one application per inbound primitive and routing style.
let routing = lazy (loadOrFail LoadOptions.defaults "Routing/Routing.slnx")

/// Real F# web frameworks as a second layer of fixtures (plan section 9): Giraffe, Falco and Oxpecker.
let frameworks = lazy (loadOrFail LoadOptions.defaults "Frameworks/Frameworks.slnx")

/// The fixture applications target net8.0 and net10.0 (plan section 9; Shop mixes the two on purpose, and the
/// Oxpecker fixture's package exists for net10.0 only). The loads above take each project's first framework,
/// net8.0; these take net10.0, and must give the same results. The first load builds both frameworks.
let private onNet10 (first: Lazy<LoadedSolution>) relative =
    lazy
        (first.Force() |> ignore
         loadOrFail { LoadOptions.defaults with Framework = Some "net10.0"; Build = false } relative)

let effectsNet10 = onNet10 effects "Effects/Effects.slnx"
let routingNet10 = onNet10 routing "Routing/Routing.slnx"
let frameworksNet10 = onNet10 frameworks "Frameworks/Frameworks.slnx"

let project (solution: LoadedSolution) (name: string) =
    solution.Projects |> List.find (fun p -> p.Name = name)
