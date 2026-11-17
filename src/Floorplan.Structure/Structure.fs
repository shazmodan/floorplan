/// Structure analysis (plan section 3.3, stage 2) behind a language-neutral interface (section 3.5).
/// An analyzer turns projects of one language into units and unit-to-unit dependencies.
namespace Floorplan.Structure

open System
open Floorplan.Model
open Floorplan.ProjectLoading

module UnitId =
    /// A unit is identified by project plus full name (plan section 3.2).
    let create (projectId: string) (fullName: string) = $"{projectId}:{fullName}"

    /// The unit that stands in for a whole project of an unsupported language.
    let forProject (projectId: string) = create projectId "*"

/// What every analyzer may look up about the rest of the solution.
type SolutionIndex =
    { Projects: LoadedProject list
      /// Projects by the simple name of the assembly they build, case-insensitive.
      ByAssembly: Map<string, LoadedProject>
      /// Languages that have a structure analyzer.
      SupportedLanguages: Set<Language> }

module SolutionIndex =
    let create (projects: LoadedProject list) (supported: Language list) =
        { Projects = projects
          ByAssembly = projects |> List.map (fun p -> p.AssemblyName.ToLowerInvariant(), p) |> Map.ofList
          SupportedLanguages = set supported }

    let tryProjectOfAssembly (assemblyName: string) (index: SolutionIndex) =
        index.ByAssembly |> Map.tryFind (assemblyName.ToLowerInvariant())

    let supportLevel (project: LoadedProject) (index: SolutionIndex) =
        if index.SupportedLanguages.Contains project.Language then
            SupportLevel.Full
        else
            SupportLevel.Limited

type StructureResult =
    { Units: CodeUnit list
      Dependencies: Dependency list
      Diagnostics: Diagnostic list }

module StructureResult =
    let empty =
        { Units = []
          Dependencies = []
          Diagnostics = [] }

    let combine (results: StructureResult list) =
        { Units = results |> List.collect (fun r -> r.Units)
          Dependencies = results |> List.collect (fun r -> r.Dependencies)
          Diagnostics = results |> List.collect (fun r -> r.Diagnostics) }

type IStructureAnalyzer =
    abstract Language: Language
    /// Analyzes all projects of this analyzer's language. Edges may point into units of other projects.
    abstract Analyze: projects: LoadedProject list * index: SolutionIndex -> StructureResult

/// Carries projects in a language without an analyzer as a single unit each (plan section 2).
module LimitedSupport =
    let unitFor (project: LoadedProject) =
        { Id = UnitId.forProject project.Id
          ProjectId = project.Id
          Kind = UnitKind.ProjectUnit
          Name = project.Name
          DisplayName = project.Name
          Parent = None
          Location = None }

    let analyze (projects: LoadedProject list) =
        { Units = projects |> List.map unitFor
          Dependencies = []
          Diagnostics =
            projects
            |> List.map (fun p ->
                { Severity = DiagnosticSeverity.Info
                  Code = "limited-support"
                  Message =
                    $"{p.Name} is a {Json.unionName p.Language} project. It is shown as a single node, and effects found in it are attributed to the project as a whole."
                  ProjectId = Some p.Id
                  Location = None }) }

module StructureAnalysis =
    /// Runs every analyzer on the projects of its language; projects in other languages get limited support.
    let analyze (analyzers: IStructureAnalyzer list) (projects: LoadedProject list) =
        let index = SolutionIndex.create projects (analyzers |> List.map (fun a -> a.Language))

        let analyzed =
            analyzers
            |> List.map (fun a ->
                let ofLanguage = projects |> List.filter (fun p -> p.Language = a.Language)
                if ofLanguage.IsEmpty then StructureResult.empty else a.Analyze(ofLanguage, index))

        let unsupported =
            projects |> List.filter (fun p -> not (index.SupportedLanguages.Contains p.Language))

        let result = StructureResult.combine (LimitedSupport.analyze unsupported :: analyzed)

        // An analyzer names an edge's target by what declares the symbol, which in another language's project
        // may be a type inside a unit: the edge goes to the innermost unit that contains it, the project's
        // root namespace ("project:") last. Edges whose ends do not exist are dropped, and duplicates that
        // different analyzers may produce are merged.
        let ids = result.Units |> List.map (fun u -> u.Id) |> set

        let rec resolve (id: string) =
            if ids.Contains id then
                Some id
            else
                let colon = id.IndexOf ':'
                let dot = id.LastIndexOf '.'

                if colon < 0 || id.Length = colon + 1 then None
                elif dot > colon then resolve (id.Substring(0, dot))
                else resolve (id.Substring(0, colon + 1))

        let dependencies =
            result.Dependencies
            |> List.choose (fun d -> resolve d.ToUnit |> Option.map (fun t -> { d with ToUnit = t }))
            |> List.filter (fun d -> ids.Contains d.FromUnit && d.FromUnit <> d.ToUnit)
            |> List.groupBy (fun d -> d.FromUnit, d.ToUnit)
            |> List.map (fun ((f, t), ds) ->
                { FromUnit = f
                  ToUnit = t
                  Weight = ds |> List.sumBy (fun d -> d.Weight)
                  CrossProject = ds.Head.CrossProject
                  SampleLocations = ds |> List.collect (fun d -> d.SampleLocations) |> List.truncate 3 })
            |> List.sortBy (fun d -> d.FromUnit, d.ToUnit)

        { result with Dependencies = dependencies }, index
