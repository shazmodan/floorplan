/// What effect analysis needs to know about the solution, independent of how it was loaded.
namespace Floorplan.Analysis

open Floorplan.Model

type AnalyzedProject =
    { ProjectId: string
      /// The built assembly.
      AssemblyPath: string
      TargetFramework: string
      IsApplication: bool
      SupportLevel: SupportLevel
      /// Where dependency and framework assemblies are found: the output folder first, then references.
      SearchPaths: string list }

type AnalysisInput =
    { Projects: AnalyzedProject list
      /// Units from structure analysis; call sites are attributed to them by source location.
      Units: CodeUnit list
      /// Extra catalog files (--catalog).
      ExtraCatalogs: string list }
