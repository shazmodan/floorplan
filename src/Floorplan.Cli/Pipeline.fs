/// Orchestration: load, analyze, lay out, render (plan section 3.3).
module Floorplan.Cli.Pipeline

open System
open System.IO
open System.Reflection
open Floorplan.Model
open Floorplan.ProjectLoading
open Floorplan.Structure
open Floorplan.Structure.FSharp
open Floorplan.Layout
open Floorplan.Rendering
open Floorplan.Cli.Options

let toolVersion =
    let assembly = Assembly.GetExecutingAssembly()

    match assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>() with
    | null -> string (assembly.GetName().Version)
    | a -> a.InformationalVersion.Split('+').[0]

type Outcome =
    { Model: AnalysisModel
      /// What was written: the site's index.html or the JSON file.
      Written: string
      /// Some projects failed to load or build (plan risk 10): the run is reported as failed.
      HadFailures: bool }

let private toProject (index: SolutionIndex) (p: LoadedProject) : Project =
    { Id = p.Id
      Name = p.Name
      Path = p.Path
      Language = p.Language
      SupportLevel = SolutionIndex.supportLevel p index
      TargetFramework = p.TargetFramework
      IsApplication = p.IsApplication
      IsTestProject = p.IsTestProject
      CatalogUsed = None
      CatalogFallback = false
      ProjectReferences = p.ProjectReferences }

let private failureDiagnostic (f: LoadFailure) =
    { Severity = DiagnosticSeverity.Error
      Code = "project-load-failed"
      Message = $"{Path.GetFileName f.Path}: {f.Message}"
      ProjectId = None
      Location = None }

/// Builds the analysis model for a loaded solution.
let buildModel (log: string -> unit) (solution: LoadedSolution) : AnalysisModel =
    log $"Analyzing structure of {solution.Projects.Length} projects"
    let structure, index = StructureAnalysis.analyze [ FSharpStructureAnalyzer.create () ] solution.Projects
    let projects = solution.Projects |> List.map (toProject index)

    log "Computing layout"
    let layout = SolutionLayout.compute projects structure.Units structure.Dependencies

    { AnalysisModel.empty toolVersion (DateTime.UtcNow.ToString "yyyy-MM-ddTHH:mm:ssZ") { Name = solution.Name; Path = solution.Path } with
        Projects = projects
        Units = structure.Units |> List.sortBy (fun u -> u.Id)
        Dependencies = structure.Dependencies
        Layout = layout
        Diagnostics = (solution.Failures |> List.map failureDiagnostic) @ structure.Diagnostics }

let run (log: string -> unit) (options: AnalyzeOptions) : Result<Outcome, string> =
    let loadOptions =
        { Configuration = options.Configuration
          Framework = options.Framework
          IncludeTests = options.IncludeTests
          Build = options.Build
          Include = options.Include
          Exclude = options.Exclude }

    log (if options.Build then "Building and loading the solution" else "Loading the solution")

    match SolutionLoader.load loadOptions options.Path with
    | Error e -> Error(SolutionLoader.describe e)
    | Ok solution when solution.Projects.IsEmpty ->
        let reasons = solution.Failures |> List.map (fun f -> $"  {Path.GetFileName f.Path}: {f.Message}") |> String.concat "\n"
        Error $"No project could be analyzed.\n{reasons}"
    | Ok solution ->
        let model = buildModel log solution

        let renderer, target =
            match options.Format with
            | Json -> JsonRenderer.create (), { Path = options.Output; SingleFile = false }
            | Site -> WebsiteRenderer.create (), { Path = options.Output; SingleFile = options.SingleFile }

        log $"Writing {renderer.Name} output"

        match renderer.Render(model, target) with
        | Error(RenderError e) -> Error $"Could not write output: {e}"
        | Ok() ->
            let written =
                match options.Format, options.SingleFile with
                | Json, _ -> JsonRenderer.outputFile target
                | Site, true when Path.GetExtension(options.Output).ToLowerInvariant() = ".html" -> options.Output
                | Site, true -> Path.Combine(options.Output, "floorplan.html")
                | Site, false -> Path.Combine(options.Output, "index.html")

            Ok
                { Model = model
                  Written = Path.GetFullPath written
                  HadFailures = not solution.Failures.IsEmpty }
