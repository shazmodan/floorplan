/// Effect analysis (plan section 3.3, stage 4): builds one call graph over every user assembly and its
/// dependencies, matches it against the boundary catalog of each project's target framework, and returns
/// exit points and diagnostics.
module Floorplan.Analysis.EffectAnalysis

open System.Collections.Generic
open Floorplan.Model
open Floorplan.IL
open Floorplan.Catalog

type EffectResult =
    { ExitPoints: ExitPoint list
      Diagnostics: Diagnostic list
      /// The catalog used for each project, by project id.
      Catalogs: Map<string, CatalogChoice> }

/// The program every analysis of one run shares: assemblies, call graph and per-catalog matchers and summaries.
type Program(input: AnalysisInput, recognizers: ICodegenRecognizer list) =
    let set =
        new AssemblySet(
            CecilReader.create (),
            input.Projects |> List.map (fun p -> p.ProjectId, p.AssemblyPath),
            input.Projects |> List.collect (fun p -> p.SearchPaths) |> List.distinct
        )

    let users =
        input.Projects |> List.choose (fun p -> set.TryLoad(System.IO.Path.GetFileNameWithoutExtension p.AssemblyPath) |> Option.map (fun a -> p, a))

    let graph =
        let g = CallGraph(set, recognizers)

        g.AddRoots(
            users
            |> Seq.collect (fun (_, a) -> a.Types)
            |> Seq.collect (fun t -> t.Methods)
            |> Seq.filter (fun m -> not m.IsAbstract)
            |> Seq.map (fun m -> m.Ref)
        )

        g

    let extras = input.ExtraCatalogs |> List.map CatalogLoader.loadFile
    let matchers = Dictionary<string, Matcher>()
    let summaries = Dictionary<string, Summaries.Summaries>()

    member _.Set = set
    member _.Graph = graph
    member _.Users = users
    member _.Recognizers = recognizers
    member _.Attributor = Attribution.Attributor(input.Units, input.Projects)

    member _.CatalogFor(project: AnalyzedProject) =
        let choice = CatalogLoader.forTargetFramework project.TargetFramework
        { choice with Catalog = CatalogLoader.extend choice.Catalog extras }

    member this.MatcherFor(project: AnalyzedProject) =
        let catalog = (this.CatalogFor project).Catalog

        match matchers.TryGetValue catalog.Name with
        | true, m -> m
        | _ ->
            let m = Matcher(catalog, set.Supertypes)
            matchers[catalog.Name] <- m
            m

    member this.SummariesFor(project: AnalyzedProject) =
        let matcher = this.MatcherFor project

        match summaries.TryGetValue matcher.Catalog.Name with
        | true, s -> s
        | _ ->
            let s = Summaries.Summaries(graph, matcher, fun m -> set.Origin m.Type.Assembly = Dependency)
            summaries[matcher.Catalog.Name] <- s
            s

    interface System.IDisposable with
        member _.Dispose() = (set :> System.IDisposable).Dispose()

let exitPoints (program: Program) (input: AnalysisInput) : EffectResult =
    let attributor = program.Attributor

    let perProject =
        program.Users
        |> List.map (fun (project, assembly) ->
            let ctx: ExitPoints.ProjectContext =
                { Project = project
                  Set = program.Set
                  Graph = program.Graph
                  Matcher = program.MatcherFor project
                  Summaries = program.SummariesFor project
                  Attributor = attributor
                  Recognizers = program.Recognizers }

            ExitPoints.analyze ctx assembly)

    let found =
        perProject
        |> List.collect fst
        |> List.sortBy (fun f -> f.SortKey)
        |> List.mapi (fun i f -> { f.ExitPoint with Id = $"exit-{i + 1}" })

    let catalogs =
        input.Projects |> List.map (fun p -> p.ProjectId, program.CatalogFor p) |> Map.ofList

    let fallback =
        catalogs
        |> Map.toList
        |> List.filter (fun (_, c) -> c.Fallback)
        |> List.map (fun (projectId, c) ->
            let tfm = (input.Projects |> List.find (fun p -> p.ProjectId = projectId)).TargetFramework

            { Severity = DiagnosticSeverity.Warning
              Code = "catalog-fallback"
              Message = $"{projectId} targets {tfm}, which is newer than every boundary catalog. The {c.Catalog.Name} catalog was used; results may miss primitives added since."
              ProjectId = Some projectId
              Location = None })

    let missing =
        input.Projects
        |> List.filter (fun p -> not (program.Users |> List.exists (fun (u, _) -> u.ProjectId = p.ProjectId)))
        |> List.map (fun p ->
            { Severity = DiagnosticSeverity.Error
              Code = "assembly-unreadable"
              Message = $"The built assembly of {p.ProjectId} could not be read: {p.AssemblyPath}"
              ProjectId = Some p.ProjectId
              Location = None })

    { ExitPoints = found
      Diagnostics = fallback @ missing @ (perProject |> List.collect snd |> List.distinct)
      Catalogs = catalogs }
