namespace Floorplan.ProjectLoading

open Floorplan.Model

/// One project of the solution, evaluated by MSBuild.
type LoadedProject =
    { /// Stable id: the project name, or the relative path when two projects share a name.
      Id: string
      Name: string
      /// Absolute path of the project file.
      Path: string
      Language: Language
      TargetFramework: string
      TargetFrameworks: string list
      /// An executable or web project (plan section 3.2).
      IsApplication: bool
      IsTestProject: bool
      /// Source files in compile order, absolute.
      SourceFiles: string list
      /// Absolute paths of referenced projects.
      ProjectReferencePaths: string list
      /// Ids of referenced projects that were loaded.
      ProjectReferences: string list
      /// Resolved reference assemblies, absolute.
      ReferencePaths: string list
      /// The built assembly.
      OutputAssembly: string
      /// The PDB next to the built assembly, if present.
      Pdb: string option
      /// The raw MSBuild evaluation, for structure analyzers that need compiler options.
      Options: Ionide.ProjInfo.Types.ProjectOptions }

type LoadOptions =
    { Configuration: string
      /// Preferred target framework for multi-targeting projects.
      Framework: string option
      IncludeTests: bool
      Build: bool
      /// Analyze only these projects (by name) and what they reference. Empty: all.
      Include: string list
      Exclude: string list }

module LoadOptions =
    let defaults =
        { Configuration = "Debug"
          Framework = None
          IncludeTests = false
          Build = true
          Include = []
          Exclude = [] }

type LoadFailure = { Path: string; Message: string }

type LoadedSolution =
    { Name: string
      /// The solution or project file that was loaded.
      Path: string
      Projects: LoadedProject list
      /// Projects left out by the test-project rule or by --include/--exclude, by name.
      Skipped: string list
      /// Projects that failed to load or build. The rest are still analyzed (plan risk 10).
      Failures: LoadFailure list }

type LoadError =
    | InputNotFound of string
    | UnsupportedInput of string
    | NoProjects of string
    | BuildToolFailed of string
