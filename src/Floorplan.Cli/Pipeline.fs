/// Orchestration: load, analyze, lay out, render (plan section 3.3).
module Floorplan.Cli.Pipeline

open System
open System.IO
open System.Reflection
open Floorplan.Model
open Floorplan.ProjectLoading
open Floorplan.Structure
open Floorplan.Structure.FSharp
open Floorplan.Structure.CSharp
open Floorplan.IL.Codegen.FSharp
open Floorplan.IL.Codegen.CSharp
open Floorplan.Analysis
open Floorplan.Layout
open Floorplan.Architecture
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

/// The failed build's output, once for the solution rather than with every project that was not built.
let private buildDiagnostic (output: string) =
    { Severity = DiagnosticSeverity.Error
      Code = "build-failed"
      Message = $"The build failed:\n{output}"
      ProjectId = None
      Location = None }

/// Where a project's dependencies are found: its output folder first (implementation assemblies), then the
/// compile-time references (reference packs for the shared frameworks).
let private searchPaths (p: LoadedProject) =
    let outputDir = Path.GetDirectoryName p.OutputAssembly

    let local =
        if Directory.Exists outputDir then
            Directory.GetFiles(outputDir, "*.dll") |> Array.sort |> Array.toList
        else
            []

    local @ p.ReferencePaths

let analysisInput (solution: LoadedSolution) (index: SolutionIndex) (units: CodeUnit list) (catalogs: string list) =
    { Projects =
        solution.Projects
        |> List.map (fun p ->
            { ProjectId = p.Id
              AssemblyPath = p.OutputAssembly
              TargetFramework = p.TargetFramework
              IsApplication = p.IsApplication
              SupportLevel = SolutionIndex.supportLevel p index
              SearchPaths = searchPaths p })
      Units = units
      ExtraCatalogs = catalogs }

/// Builds the analysis model for a loaded solution.
let buildModel (log: string -> unit) (store: Caching.Store) (catalogs: string list) (show: EffectCategory list) (solution: LoadedSolution) : AnalysisModel =
    log $"Analyzing structure of {solution.Projects.Length} projects"
    let structure, index = StructureAnalysis.analyze [ FSharpStructureAnalyzer.create store; CSharpStructureAnalyzer.create store ] solution.Projects

    log "Analyzing effects"
    let input = analysisInput solution index structure.Units catalogs
    use program = new EffectAnalysis.Program(input, [ FSharpCodegen.recognizer; CSharpCodegen.recognizer ], store)

    if not program.CachedAssemblies.IsEmpty then
        log $"Using cached summaries for {program.CachedAssemblies.Length} dependencies"

    let effects = EffectAnalysis.exitPoints program input
    program.SaveCache()

    log "Finding entry points"
    let entries = EntryPointAnalysis.analyze program input
    log $"Tracing {entries.Length} entry points to the {effects.ExitPoints.Length} exit points they reach"
    let traces = Traces.compute program entries effects.ExitPoints

    let projects =
        solution.Projects
        |> List.map (toProject index)
        |> List.map (fun p ->
            match effects.Catalogs.TryFind p.Id with
            | Some c ->
                { p with
                    CatalogUsed = Some c.Catalog.Name
                    CatalogFallback = c.Fallback }
            | None -> p)

    log "Computing layout"
    let presentation =
        { HiddenCategories =
            [ EffectCategory.Environment; EffectCategory.Nondeterminism ]
            |> List.filter (fun c -> not (List.contains c show))
          HiddenConfidence = [ Confidence.Low ] }

    let layout =
        { SolutionLayout.compute projects structure.Units structure.Dependencies with
            FlowGraph = SolutionLayout.flow structure.Units structure.Dependencies entries effects.ExitPoints presentation }

    let model =
        { AnalysisModel.empty toolVersion (DateTime.UtcNow.ToString "yyyy-MM-ddTHH:mm:ssZ") { Name = solution.Name; Path = solution.Path } with
            Projects = projects
            Units = structure.Units |> List.sortBy (fun u -> u.Id)
            Dependencies = structure.Dependencies
            TypeDependencies = structure.TypeDependencies
            EntryPoints = entries
            Traces = traces
            ExitPoints = effects.ExitPoints
            Layout = layout
            Presentation = presentation
            Diagnostics =
                (solution.BuildOutput |> Option.map buildDiagnostic |> Option.toList)
                @ (solution.Failures |> List.map failureDiagnostic)
                @ structure.Diagnostics
                @ effects.Diagnostics }

    log "Deriving the C4 model"
    { model with Architecture = C4.derive model }

let private loadOptionsOf (options: AnalyzeOptions) =
    { Configuration = options.Configuration
      Framework = options.Framework
      IncludeTests = options.IncludeTests
      Build = options.Build
      Include = options.Include
      Exclude = options.Exclude }

let private noProjects (solution: LoadedSolution) =
    let reasons = solution.Failures |> List.map (fun f -> $"  {Path.GetFileName f.Path}: {f.Message}") |> String.concat "\n"

    let build =
        solution.BuildOutput |> Option.map (fun o -> $"\nThe build failed:\n{o}") |> Option.defaultValue ""

    $"No project could be analyzed.\n{reasons}{build}"

let run (log: string -> unit) (options: AnalyzeOptions) : Result<Outcome, string> =
    let loadOptions = loadOptionsOf options

    match SolutionLoader.load loadOptions options.Path with
    | Error e -> Error(SolutionLoader.describe e)
    | Ok solution when solution.Projects.IsEmpty -> Error(noProjects solution)
    | Ok solution ->
        let store = if options.Cache then Caching.Store(Some(Caching.defaultDirectory ())) else Caching.Store.Disabled
        let model = buildModel log store options.Catalogs options.Show solution

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

/// The solutions to analyze in a folder of solutions (docs/landscape-plan.md), when the path is one: a folder with
/// no solution or project of its own, with solutions in its subfolders.
let folderOfSolutions (options: AnalyzeOptions) : Discovery.FoundSolutions option =
    if Discovery.isFolderOfSolutions options.Path then
        let found = Discovery.findSolutions (defaultArg options.Path Environment.CurrentDirectory)
        if found.Solutions.IsEmpty && found.Nested.IsEmpty then None else Some found
    else
        None

type LandscapeOutcome =
    { Landscape: Landscape
      /// Each solution as analyzed, in the landscape's order.
      Analyzed: Landscape.Analyzed list
      /// The landscape site's index.html or landscape.json.
      Written: string
      /// Some solution, or some project of one, could not be analyzed.
      HadFailures: bool }

/// The solutions chosen with --solutions and --exclude-solutions, by name without extension (any case): named ones
/// from every solution found, nested ones too; without --solutions, those found.
let chooseSolutions (options: AnalyzeOptions) (found: Discovery.FoundSolutions) =
    let name (path: string) = Path.GetFileNameWithoutExtension(path).ToLowerInvariant()
    let lower = List.map (fun (s: string) -> s.ToLowerInvariant()) >> Set.ofList
    let named, excluded = lower options.Solutions, lower options.ExcludeSolutions

    let chosen =
        if named.IsEmpty then found.Solutions
        else found.Solutions @ found.Nested |> List.filter (fun p -> named.Contains(name p))

    let unknown =
        named - Set.ofList ((found.Solutions @ found.Nested) |> List.map name) |> Set.toList

    if not unknown.IsEmpty then
        let known = found.Solutions @ found.Nested |> List.map Path.GetFileNameWithoutExtension |> String.concat ", "
        let names = String.concat ", " unknown
        Error $"No solution named {names}. Solutions found: {known}."
    else
        match chosen |> List.filter (fun p -> not (excluded.Contains(name p))) with
        | [] -> Error "No solution left to analyze."
        | some -> Ok some

/// Writes a landscape: each solution's site (or JSON) under solutions/<slug>/ and the landscape beside them, or,
/// with --single-file, all of it in one HTML file. The landscape site's index.html, landscape.json or the file.
let writeLandscape (options: AnalyzeOptions) (landscape: Landscape) (analyzed: Landscape.Analyzed list) : Result<string, string> =
    let link (slug: string) : WebsiteRenderer.LandscapeLink =
        { Landscape = landscape.Name
          Current = slug
          Solutions = landscape.Solutions |> List.map (fun s -> s.Name, s.Slug) }

    // Each solution's model; its own C4 model shows the other systems of the landscape it talks to, not hosts.
    let models =
        [ for s, a in List.zip landscape.Solutions analyzed do
              match a.Model with
              | Ok model ->
                  let known = Landscape.knownOf landscape s.Slug

                  let model =
                      if known.Sends.IsEmpty && known.Receives.IsEmpty then model
                      else { model with Architecture = C4.deriveIn known model }

                  yield s.Slug, model
              | Error _ -> () ]

    try
        match options.Format with
        | Site when options.SingleFile ->
            WebsiteRenderer.renderLandscapeFile landscape models options.Output WebsiteRenderer.singleFileLimit
            |> Result.map Path.GetFullPath
        | Json ->
            for slug, model in models do
                JsonRenderer.create().Render(model, { Path = Path.Combine(options.Output, "solutions", slug); SingleFile = false }) |> ignore

            let file = Path.Combine(options.Output, "landscape.json")
            Directory.CreateDirectory options.Output |> ignore
            File.WriteAllText(file, Json.serializeValue true landscape)
            Ok(Path.GetFullPath file)
        | Site ->
            for slug, model in models do
                WebsiteRenderer.renderIn (Some(link slug)) model { Path = Path.Combine(options.Output, "solutions", slug); SingleFile = false } |> ignore

            Ok(Path.GetFullPath(WebsiteRenderer.renderLandscape landscape options.Output))
    with e ->
        Error $"Could not write output: {e.Message}"

/// Analyzes every solution of a folder of solutions, one after the other, and writes each one's site (or JSON) under
/// solutions/<slug>/, and the landscape beside them.
let runLandscape (log: string -> unit) (options: AnalyzeOptions) (found: Discovery.FoundSolutions) : Result<LandscapeOutcome, string> =
    match chooseSolutions options found with
    | Error e -> Error e
    | Ok files ->
        let root = Path.GetFullPath(defaultArg options.Path Environment.CurrentDirectory)
        let store = if options.Cache then Caching.Store(Some(Caching.defaultDirectory ())) else Caching.Store.Disabled
        let loadOptions = loadOptionsOf options
        let mutable failures = false

        let analyzed =
            files
            |> List.mapi (fun i file ->
                let name = Path.GetFileNameWithoutExtension file
                let prefix = $"Solution {i + 1} of {files.Length}, {name}"
                log prefix
                let say (message: string) = log $"{prefix}: {message}"

                let model =
                    try
                        match SolutionLoader.load loadOptions (Some file) with
                        | Error e -> Error(SolutionLoader.describe e)
                        | Ok solution when solution.Projects.IsEmpty -> Error(noProjects solution)
                        | Ok solution ->
                            if not solution.Failures.IsEmpty then failures <- true
                            Ok(buildModel say store options.Catalogs options.Show solution)
                    with e ->
                        Error $"The analysis failed: {e.Message}"

                if Result.isError model then failures <- true
                ({ File = file; Model = model }: Landscape.Analyzed))

        log "Deriving the landscape"
        let generatedAt = DateTime.UtcNow.ToString "yyyy-MM-ddTHH:mm:ssZ"
        let file, fileDiagnostics = Landscape.readFile root
        let landscape = Landscape.derive toolVersion generatedAt root file found.Nested analyzed
        let landscape = { landscape with Diagnostics = landscape.Diagnostics @ fileDiagnostics }
        log "Writing the landscape"

        match writeLandscape options landscape analyzed with
        | Ok written ->
            Ok
                { Landscape = landscape
                  Analyzed = analyzed
                  Written = written
                  HadFailures = failures }
        | Error e -> Error e

/// Diagnostics as printed after a run: errors in full; warnings of one kind up to `perCode`, then how many more
/// there are. Every one is in the output (the site's diagnostics, the JSON model).
let consoleDiagnostics (perCode: int) (diagnostics: Diagnostic list) : string list =
    let severity (d: Diagnostic) = Json.unionName d.Severity

    diagnostics
    |> List.filter (fun d -> d.Severity <> DiagnosticSeverity.Info)
    |> List.groupBy (fun d -> d.Severity = DiagnosticSeverity.Error, d.Code)
    |> List.collect (fun ((isError, code), ds) ->
        if isError || ds.Length <= perCode then
            ds |> List.map (fun d -> $"{severity d}: {d.Message}")
        else
            (ds |> List.truncate perCode |> List.map (fun d -> $"{severity d}: {d.Message}"))
            @ [ $"{severity ds.Head}: ... and {ds.Length - perCode} more like these ({code}); the output lists them all." ])
