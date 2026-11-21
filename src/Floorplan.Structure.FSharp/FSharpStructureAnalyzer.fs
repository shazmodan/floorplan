/// The F# structure analyzer: type-checks each project with FCS and turns every symbol use into an edge
/// from the enclosing module to the declaring module (plan section 3.3, stage 2).
namespace Floorplan.Structure.FSharp

open System
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Text
open Floorplan.Model
open Floorplan.ProjectLoading
open Floorplan.Structure

module FSharpStructureAnalyzer =
    let private isGenerated (file: string) =
        let sep = string Path.DirectorySeparatorChar
        file.Contains(sep + "obj" + sep) || file.EndsWith ".fsi"

    let private toLocation (r: range) =
        { File = r.FileName
          StartLine = r.StartLine
          StartColumn = r.StartColumn
          EndLine = r.EndLine
          EndColumn = r.EndColumn }

    /// FCS options from the MSBuild evaluation. Referenced projects are read from their built assemblies,
    /// which the loader has already produced, so no project is type-checked twice.
    let projectOptions (project: LoadedProject) : FSharpProjectOptions =
        let options = Ionide.ProjInfo.FCS.mapToFSharpProjectOptions project.Options Seq.empty

        { options with
            ReferencedProjects = [||] }

    let private analyzeProject (checker: FSharpChecker) (index: SolutionIndex) (project: LoadedProject) =
        let options = projectOptions project
        let sources = options.SourceFiles |> Array.filter (fun f -> f.EndsWith ".fs" && not (isGenerated f))
        let parsingOptions, _ = checker.GetParsingOptionsFromProjectOptions options

        let declared =
            sources
            |> Array.toList
            |> List.collect (fun file ->
                let text = SourceText.ofString (File.ReadAllText file)
                let parsed = checker.ParseFile(file, text, parsingOptions) |> Async.RunSynchronously
                ModuleTree.ofParsedInput parsed.ParseTree)
            |> List.distinctBy (fun u -> u.FullName)

        let unitId = UnitId.create project.Id

        let units =
            declared
            |> List.map (fun u ->
                { Id = unitId u.FullName
                  ProjectId = project.Id
                  Kind = u.Kind
                  Name = u.FullName
                  DisplayName = u.FullName.Split('.') |> Array.last
                  Parent = u.Parent |> Option.map unitId
                  Location = Some(toLocation u.Range)
                  Types = [] })

        let results = checker.ParseAndCheckProject options |> Async.RunSynchronously

        let errors =
            results.Diagnostics
            |> Array.filter (fun d -> d.Severity = FSharpDiagnosticSeverity.Error)
            |> Array.truncate 5
            |> Array.map (fun d ->
                { Severity = DiagnosticSeverity.Warning
                  Code = "structure.type-check-error"
                  Message = $"{project.Name}: {d.Message}"
                  ProjectId = Some project.Id
                  Location = Some(toLocation d.Range) })
            |> Array.toList

        /// Maps a declaring module name in some assembly to a unit id of the solution, if it is one.
        let targetUnit (symbolAssembly: string) (owner: string) =
            match SolutionIndex.tryProjectOfAssembly symbolAssembly index with
            | None -> None
            | Some target when SolutionIndex.supportLevel target index = SupportLevel.Limited ->
                Some(UnitId.forProject target.Id, target.Id)
            | Some target -> Some(UnitId.create target.Id owner, target.Id)

        let edges =
            results.GetAllUsesOfAllSymbols()
            |> Array.filter (fun u -> not u.IsFromDefinition && not (isGenerated u.FileName))
            |> Array.choose (fun u ->
                let from = ModuleTree.enclosing declared u.FileName u.Range.Start
                let owner = SymbolOwner.ofSymbol u.Symbol
                let assembly = SymbolOwner.assemblyOf u.Symbol

                match from, owner, assembly with
                | Some from, Some owner, Some assembly ->
                    targetUnit assembly owner
                    |> Option.map (fun (target, targetProject) -> unitId from.FullName, target, targetProject, u.Range)
                | _ -> None)
            |> Array.filter (fun (from, target, _, _) -> from <> target)
            |> Array.groupBy (fun (from, target, _, _) -> from, target)
            |> Array.map (fun ((from, target), uses) ->
                let _, _, targetProject, _ = uses[0]

                { FromUnit = from
                  ToUnit = target
                  Weight = uses.Length
                  CrossProject = targetProject <> project.Id
                  SampleLocations =
                    uses
                    |> Array.truncate 3
                    |> Array.map (fun (_, _, _, r) -> toLocation r)
                    |> Array.toList })
            |> Array.toList

        { Units = units
          Dependencies = edges
          Diagnostics = errors }

    /// What a project's structure depends on: its sources, compiler options, referenced assemblies, and which
    /// assemblies belong to which project of the solution.
    let private cacheKey (index: SolutionIndex) (project: LoadedProject) =
        let options = projectOptions project

        let references =
            options.OtherOptions
            |> Array.choose (fun o -> if o.StartsWith "-r:" then Some(o.Substring 3) else None)
            |> Array.map (fun r ->
                let info = FileInfo r
                if info.Exists then $"{r}|{info.Length}|{info.LastWriteTimeUtc.Ticks}" else r)

        Caching.hash
            [ yield "structure-v2"
              yield typeof<StructureResult>.Assembly.GetName().Version.ToString()
              yield project.Id
              yield project.Path
              for f in options.SourceFiles do
                  yield f
                  yield (if File.Exists f then Caching.fileHash f else "")
              yield! options.OtherOptions |> Array.filter (fun o -> not (o.StartsWith "-r:"))
              yield! references
              for p in index.Projects do
                  yield $"{p.AssemblyName}={p.Id}:{SolutionIndex.supportLevel p index}" ]

    let create (store: Caching.Store) =
        let checker = FSharpChecker.Create(projectCacheSize = 4)

        { new IStructureAnalyzer with
            member _.Language = Language.FSharp

            member _.Analyze(projects, index) =
                projects
                |> List.map (fun p ->
                    try
                        let key = cacheKey index p

                        match store.TryRead<StructureResult>("structure", key) with
                        | Some cached ->
                            Progress.report $"Structure of {p.Name}: cached"
                            cached
                        | None ->
                            Progress.report $"Structure of {p.Name}: type-checking its F# code"
                            let result = analyzeProject checker index p

                            // Results with type-check errors may change once the code is fixed: not cached.
                            if result.Diagnostics.IsEmpty then
                                store.Write("structure", key, result)

                            result
                    with e ->
                        { StructureResult.empty with
                            Diagnostics =
                                [ { Severity = DiagnosticSeverity.Error
                                    Code = "structure.failed"
                                    Message = $"{p.Name}: structure analysis failed: {e.Message}"
                                    ProjectId = Some p.Id
                                    Location = None } ] })
                |> StructureResult.combine }
