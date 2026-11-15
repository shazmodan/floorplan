/// Reads a solution through MSBuild (Ionide.ProjInfo) and returns everything later stages need per project
/// (plan section 3.3, stage 1).
module Floorplan.ProjectLoading.SolutionLoader

open System
open System.IO
open Ionide.ProjInfo
open Ionide.ProjInfo.Types
open Floorplan.Model

let private samePath (a: string) (b: string) =
    System.String.Equals(Path.GetFullPath a, Path.GetFullPath b, StringComparison.OrdinalIgnoreCase)

let private property (options: ProjectOptions) (name: string) =
    options.AllProperties |> Map.tryFind name |> Option.bind Seq.tryHead |> Option.defaultValue ""

let private isTrue (value: string) =
    System.String.Equals(value, "true", StringComparison.OrdinalIgnoreCase)

let languageOf (path: string) =
    match Discovery.extensionOf path with
    | ".fsproj" -> Language.FSharp
    | ".csproj" -> Language.CSharp
    | _ -> Language.OtherLanguage

let private referencePaths (options: ProjectOptions) =
    options.OtherOptions
    |> List.choose (fun o ->
        if o.StartsWith "-r:" then Some(o.Substring 3)
        elif o.StartsWith "/reference:" then Some(o.Substring 11)
        elif o.StartsWith "/r:" then Some(o.Substring 3)
        else None)
    |> List.map (fun p -> p.Trim '"')
    |> List.distinct

/// Gives every project a readable, unique id: its name, or its solution-relative path on a name clash.
let private assignIds (root: string) (projects: ProjectOptions list) =
    let name (p: ProjectOptions) =
        Path.GetFileNameWithoutExtension p.ProjectFileName

    let clashes =
        projects
        |> List.countBy name
        |> List.filter (fun (_, n) -> n > 1)
        |> List.map fst
        |> set

    projects
    |> List.map (fun p ->
        let id =
            if clashes.Contains(name p) then
                Path.GetRelativePath(root, p.ProjectFileName).Replace('\\', '/')
            else
                name p

        p.ProjectFileName, id)
    |> Map.ofList

let private toLoaded (ids: Map<string, string>) (options: ProjectOptions) =
    let outputType = property options "OutputType"
    let isWeb = isTrue (property options "UsingMicrosoftNETSdkWeb")
    let output = options.TargetPath
    let pdb = Path.ChangeExtension(output, ".pdb")

    let references =
        options.ReferencedProjects |> List.map (fun r -> Path.GetFullPath r.ProjectFileName)

    { Id = ids[options.ProjectFileName]
      Name = Path.GetFileNameWithoutExtension options.ProjectFileName
      Path = Path.GetFullPath options.ProjectFileName
      Language = languageOf options.ProjectFileName
      TargetFramework = options.TargetFramework
      TargetFrameworks = options.ProjectSdkInfo.TargetFrameworks
      IsApplication =
        isWeb
        || System.String.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase)
        || System.String.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase)
      IsTestProject = options.ProjectSdkInfo.IsTestProject || isTrue (property options "IsTestProject")
      SourceFiles = options.SourceFiles |> List.map Path.GetFullPath
      ProjectReferencePaths = references
      ProjectReferences =
        references
        |> List.choose (fun r -> ids |> Map.tryPick (fun path id -> if samePath path r then Some id else None))
      ReferencePaths = referencePaths options
      OutputAssembly = output
      AssemblyName = Path.GetFileNameWithoutExtension output
      Pdb = if File.Exists pdb then Some pdb else None
      Options = options }

/// Keeps the named projects and everything they reference.
let private includeClosure (names: string list) (projects: LoadedProject list) =
    if names.IsEmpty then
        projects
    else
        let byPath = projects |> List.map (fun p -> p.Path.ToLowerInvariant(), p) |> Map.ofList

        let rec visit (seen: Set<string>) (p: LoadedProject) =
            let key = p.Path.ToLowerInvariant()

            if seen.Contains key then
                seen
            else
                p.ProjectReferencePaths
                |> List.choose (fun r -> byPath |> Map.tryFind (r.ToLowerInvariant()))
                |> List.fold visit (seen.Add key)

        let wanted =
            projects
            |> List.filter (fun p -> names |> List.exists (fun n -> System.String.Equals(n, p.Name, StringComparison.OrdinalIgnoreCase)))
            |> List.fold visit Set.empty

        projects |> List.filter (fun p -> wanted.Contains(p.Path.ToLowerInvariant()))

let private matchesName (names: string list) (p: LoadedProject) =
    names |> List.exists (fun n -> System.String.Equals(n, p.Name, StringComparison.OrdinalIgnoreCase))

let load (options: LoadOptions) (input: string option) : Result<LoadedSolution, LoadError> =
    match Discovery.resolve input with
    | Error e -> Error e
    | Ok path ->
        let root = Path.GetDirectoryName path

        let prepared =
            if options.Build then Build.build path options.Configuration else Build.restore path

        // A failed build is not fatal: projects that did build are still analyzed (plan risk 10).
        let buildFailed = prepared.ExitCode <> 0

        let tools =
            try
                Ok(Init.init (DirectoryInfo root) None)
            with e ->
                Error(BuildToolFailed $"Could not locate MSBuild: {e.Message}")

        match tools with
        | Error e -> Error e
        | Ok tools ->
            let globalProperties =
                [ "Configuration", options.Configuration
                  yield! options.Framework |> Option.map (fun f -> "TargetFramework", f) |> Option.toList ]

            let loader = WorkspaceLoader.Create(tools, globalProperties)

            let evaluated =
                if Discovery.isProject path then
                    loader.LoadProjects [ path ]
                else
                    loader.LoadSln path
                |> Seq.filter (fun p -> Discovery.isProject p.ProjectFileName)
                |> Seq.distinctBy (fun p -> p.ProjectFileName.ToLowerInvariant())
                |> Seq.toList

            if evaluated.IsEmpty then
                let detail =
                    if buildFailed then $"\n{prepared.Output.Trim()}" else ""

                Error(NoProjects $"No projects could be loaded from {path}.{detail}")
            else
                let ids = assignIds root evaluated
                let all = evaluated |> List.map (toLoaded ids)

                let afterTests =
                    all |> List.filter (fun p -> options.IncludeTests || not p.IsTestProject)

                let selected =
                    afterTests
                    |> includeClosure options.Include
                    |> List.filter (fun p -> not (matchesName options.Exclude p))

                let built, notBuilt =
                    selected |> List.partition (fun p -> File.Exists p.OutputAssembly)

                let failures =
                    notBuilt
                    |> List.map (fun p ->
                        { Path = p.Path
                          Message =
                            if buildFailed then
                                $"The project did not build.\n{prepared.Output.Trim()}"
                            else
                                $"Built assembly not found: {p.OutputAssembly}. Build the solution, or run without --no-build." })

                let keptPaths = built |> List.map (fun p -> p.Path) |> set

                let built =
                    built
                    |> List.map (fun p ->
                        { p with
                            ProjectReferences =
                                p.ProjectReferences
                                |> List.filter (fun id -> built |> List.exists (fun b -> b.Id = id && keptPaths.Contains b.Path)) })

                Ok
                    { Name = Path.GetFileNameWithoutExtension path
                      Path = path
                      Projects = built
                      Skipped =
                        all
                        |> List.filter (fun p -> not (selected |> List.exists (fun s -> s.Path = p.Path)))
                        |> List.map (fun p -> p.Name)
                      Failures = failures }

let describe (error: LoadError) =
    match error with
    | InputNotFound m
    | UnsupportedInput m
    | NoProjects m
    | BuildToolFailed m -> m
