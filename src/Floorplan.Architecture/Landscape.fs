/// A folder of solutions as one landscape of systems (docs/landscape-plan.md): each solution a system, derived from
/// the solutions' analysis models as `C4` derives one solution's model from its analysis.
module Floorplan.Architecture.Landscape

open System
open System.IO
open Floorplan.Model

/// A solution of the landscape as analyzed: its file, and its model when it could be analyzed.
type Analyzed =
    { /// The solution file, absolute.
      File: string
      Model: Result<AnalysisModel, string> }

/// A name as a folder and id: lower case, with anything but letters, digits, dots, dashes and underscores as "-".
let slugOf (name: string) =
    let chars =
        name.ToLowerInvariant()
        |> Seq.map (fun c -> if Char.IsAsciiLetterOrDigit c || c = '.' || c = '-' || c = '_' then c else '-')
        |> Seq.toArray

    match String(chars).Trim('-') with
    | "" -> "solution"
    | slug -> slug

/// Slugs for names, unique: a second "shop" is "shop-2".
let slugsOf (names: string list) =
    names
    |> List.mapFold
        (fun (taken: Set<string>) name ->
            let first = slugOf name
            let slug = Seq.initInfinite (fun i -> if i = 0 then first else $"{first}-{i + 1}") |> Seq.find (taken.Contains >> not)
            slug, taken.Add slug)
        Set.empty
    |> fst

/// The id of a solution's system.
let systemId (slug: string) = "system:" + slug

/// The exit points the website shows by default.
let private shown (model: AnalysisModel) (x: ExitPoint) =
    not (model.Presentation.HiddenCategories |> List.contains x.Category)
    && not (model.Presentation.HiddenConfidence |> List.contains x.Confidence)

/// A path relative to the analyzed folder, with forward slashes on every system.
let private relative (root: string) (path: string) =
    Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')

let private summary (root: string) (slug: string) (a: Analyzed) : LandscapeSolution =
    let path = relative root a.File
    let name = Path.GetFileNameWithoutExtension a.File

    match a.Model with
    | Error failure ->
        { Name = name
          Slug = slug
          Path = path
          Languages = []
          Frameworks = []
          Applications = 0
          Projects = 0
          EntryPoints = 0
          ExitPoints = 0
          Errors = 1
          Warnings = 0
          Failure = Some failure }
    | Ok m ->
        let projects = m.Projects |> List.filter (fun p -> not p.IsTestProject)
        let count severity = m.Diagnostics |> List.filter (fun d -> d.Severity = severity) |> List.length

        { Name = m.Solution.Name
          Slug = slug
          Path = path
          Languages = projects |> List.map (fun p -> p.Language) |> List.distinct |> List.sort
          Frameworks = projects |> List.map (fun p -> p.TargetFramework) |> List.distinct |> List.sort
          Applications = projects |> List.filter (fun p -> p.IsApplication) |> List.length
          Projects = projects.Length
          EntryPoints = m.EntryPoints.Length
          ExitPoints = m.ExitPoints |> List.filter (shown m) |> List.length
          Errors = count DiagnosticSeverity.Error
          Warnings = count DiagnosticSeverity.Warning
          Failure = None }

/// Every solution's errors and warnings, named after the solution, and why a solution could not be analyzed.
let private diagnosticsOf (solutions: (LandscapeSolution * Analyzed) list) =
    solutions
    |> List.collect (fun (s, a) ->
        match a.Model with
        | Error failure ->
            [ { Severity = DiagnosticSeverity.Error
                Code = "solution-failed"
                Message = $"[{s.Name}] {failure}"
                ProjectId = None
                Location = None } ]
        | Ok m ->
            m.Diagnostics
            |> List.filter (fun d -> d.Severity <> DiagnosticSeverity.Info)
            |> List.map (fun d -> { d with Message = $"[{s.Name}] {d.Message}" }))

/// The landscape of the solutions found in a folder.
let derive (toolVersion: string) (generatedAt: string) (root: string) (nested: string list) (analyzed: Analyzed list) : Landscape =
    let root = Path.GetFullPath root
    let slugs = slugsOf (analyzed |> List.map (fun a -> Path.GetFileNameWithoutExtension a.File))
    let solutions = List.zip slugs analyzed |> List.map (fun (slug, a) -> summary root slug a, a)

    { SchemaVersion = AnalysisModel.CurrentSchemaVersion
      ToolVersion = toolVersion
      GeneratedAt = generatedAt
      Name = (match Path.GetFileName(root.TrimEnd('\\', '/')) with "" -> root | n -> n)
      Path = root
      Solutions = solutions |> List.map fst
      Nested = nested |> List.map (relative root)
      Architecture = AnalysisModel.emptyArchitecture
      Interactions = []
      Sources = []
      Diagnostics = diagnosticsOf solutions }
