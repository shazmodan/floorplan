/// The analysis model (plan section 7.1). One immutable, serializable document. Renderers read it and
/// nothing else. Nothing here is specific to F#: the graph's nodes are *units*, whose kind says what they are.
namespace Floorplan.Model

[<RequireQualifiedAccess>]
type Language =
    | FSharp
    | CSharp
    | OtherLanguage

/// How well Floorplan understands a project. `Limited` projects appear as a single unit.
[<RequireQualifiedAccess>]
type SupportLevel =
    | Full
    | Limited

[<RequireQualifiedAccess>]
type UnitKind =
    | Module
    | Namespace
    | Type
    /// Stands in for a whole project whose language has no structure analyzer.
    | ProjectUnit

/// A source range. Lines are 1-based, columns 0-based, as in the F# compiler service.
type Location =
    { File: string
      StartLine: int
      StartColumn: int
      EndLine: int
      EndColumn: int }

type Project =
    { Id: string
      Name: string
      Path: string
      Language: Language
      SupportLevel: SupportLevel
      TargetFramework: string
      IsApplication: bool
      IsTestProject: bool
      /// The boundary catalog used for this project's assemblies, e.g. "net8.0".
      CatalogUsed: string option
      /// True when the project targets a .NET version newer than every catalog (plan section 8.2).
      CatalogFallback: bool
      ProjectReferences: string list }

type CodeUnit =
    { Id: string
      ProjectId: string
      Kind: UnitKind
      /// Fully qualified name, e.g. "MyApp.Orders.Handlers".
      Name: string
      /// Last name segment, for labels.
      DisplayName: string
      Parent: string option
      Location: Location option }

type Dependency =
    { FromUnit: string
      ToUnit: string
      /// Number of symbol uses behind this edge.
      Weight: int
      CrossProject: bool
      SampleLocations: Location list }

[<RequireQualifiedAccess>]
type EffectCategory =
    | HttpIn
    | HttpOut
    | Network
    | Database
    | FilesystemRead
    | FilesystemWrite
    | Process
    | Native
    | Environment
    | Nondeterminism

/// One step of the evidence behind a result: a call, a comparison or a lookup.
type EvidenceStep =
    { Description: string
      /// The method the step happens in, as "Namespace.Type::Method".
      Member: string
      Assembly: string
      Location: Location option }

type Evidence = { Steps: EvidenceStep list }

/// What part of a request a predicate constrains.
[<RequireQualifiedAccess>]
type RequestPart =
    | Method
    | Path
    | PathSegment
    | SegmentCount
    | Host
    | Query
    | Header
    | Body

[<RequireQualifiedAccess>]
type PredicateOperator =
    | Equals
    | StartsWith
    | EndsWith
    | Contains
    | Matches
    /// The value is captured as a route parameter rather than compared.
    | Parameter
    | NotEquals

type Predicate =
    { Part: RequestPart
      Operator: PredicateOperator
      /// For PathSegment: the 0-based segment index.
      Index: int option
      Value: string
      Location: Location option }

[<RequireQualifiedAccess>]
type Completeness =
    | Complete
    | Partial
    | Unresolved

/// How the route of an entry point was recovered (plan section 6.4).
[<RequireQualifiedAccess>]
type RouteForm =
    | ControlFlow
    | TableLookup
    | PlatformRouting

type EntryPoint =
    { Id: string
      ApplicationId: string
      HandlerUnit: string
      HandlerMember: string
      Location: Location option
      /// Category of the inbound primitive, normally HttpIn or Network.
      Category: EffectCategory
      Predicates: Predicate list
      HttpMethod: string option
      Route: string option
      Completeness: Completeness
      Forms: RouteForm list
      Evidence: Evidence }

[<RequireQualifiedAccess>]
type PayloadKind =
    | Literal
    | Template
    | Dynamic

type Payload =
    { Role: string
      Kind: PayloadKind
      Value: string option }

/// How directly an exit point was established (plan risk 2): a direct call to a primitive is high; a long path
/// through a dependency, especially through virtual calls, is low.
[<RequireQualifiedAccess>]
type Confidence =
    | High
    | Medium
    | Low

type ExitPoint =
    { Id: string
      Category: EffectCategory
      Unit: string
      /// The user method that contains the call site.
      Member: string
      Location: Location option
      /// The catalog rule that matched, e.g. "http-out.httpclient".
      RuleId: string
      /// The base-library member that performs the effect.
      Primitive: string
      Payload: Payload option
      /// The dependency assembly the call went through, if any.
      ViaAssembly: string option
      Confidence: Confidence
      Evidence: Evidence }

type Trace =
    { EntryPointId: string
      ExitPointIds: string list }

type Point = { X: float; Y: float }

type NodeLayout =
    { Id: string
      X: float
      Y: float
      Width: float
      Height: float
      Layer: int
      /// Ids of the units drawn as this node when a cycle was collapsed into one node.
      Members: string list }

type EdgeLayout =
    { From: string
      To: string
      Points: Point list }

type GraphLayout =
    { Width: float
      Height: float
      Nodes: NodeLayout list
      Edges: EdgeLayout list }

type Layout =
    { /// Projects and their references.
      ProjectGraph: GraphLayout
      /// Every unit, inside its project's box, in one coordinate space.
      UnitGraph: GraphLayout
      /// The project boxes of UnitGraph; node ids are project ids.
      ProjectBoxes: NodeLayout list }

[<RequireQualifiedAccess>]
type DiagnosticSeverity =
    | Info
    | Warning
    | Error

type Diagnostic =
    { Severity: DiagnosticSeverity
      /// Stable code, e.g. "catalog-fallback", "blind-spot.reflection", "project-load-failed".
      Code: string
      Message: string
      ProjectId: string option
      Location: Location option }

type SolutionInfo = { Name: string; Path: string }

/// Defaults for how renderers present the results; they never change the results themselves.
type Presentation =
    { /// Categories hidden unless the viewer asks for them (plan section 4.2; --show removes some).
      HiddenCategories: EffectCategory list
      /// Exit points at or below this confidence are hidden by default.
      HiddenConfidence: Confidence list }

type AnalysisModel =
    { SchemaVersion: int
      ToolVersion: string
      GeneratedAt: string
      Solution: SolutionInfo
      Projects: Project list
      Units: CodeUnit list
      Dependencies: Dependency list
      EntryPoints: EntryPoint list
      ExitPoints: ExitPoint list
      Traces: Trace list
      Layout: Layout
      Presentation: Presentation
      Diagnostics: Diagnostic list }

module AnalysisModel =
    [<Literal>]
    let CurrentSchemaVersion = 1

    let emptyGraph =
        { Width = 0.0
          Height = 0.0
          Nodes = []
          Edges = [] }

    let emptyLayout =
        { ProjectGraph = emptyGraph
          UnitGraph = emptyGraph
          ProjectBoxes = [] }

    let empty toolVersion generatedAt solution =
        { SchemaVersion = CurrentSchemaVersion
          ToolVersion = toolVersion
          GeneratedAt = generatedAt
          Solution = solution
          Projects = []
          Units = []
          Dependencies = []
          EntryPoints = []
          ExitPoints = []
          Traces = []
          Layout = emptyLayout
          Presentation =
            { HiddenCategories = [ EffectCategory.Environment; EffectCategory.Nondeterminism ]
              HiddenConfidence = [ Confidence.Low ] }
          Diagnostics = [] }

module EffectCategory =
    let all =
        [ EffectCategory.HttpIn
          EffectCategory.HttpOut
          EffectCategory.Network
          EffectCategory.Database
          EffectCategory.FilesystemRead
          EffectCategory.FilesystemWrite
          EffectCategory.Process
          EffectCategory.Native
          EffectCategory.Environment
          EffectCategory.Nondeterminism ]

    /// The catalog's spelling, e.g. "filesystem-read".
    let name =
        function
        | EffectCategory.HttpIn -> "http-in"
        | EffectCategory.HttpOut -> "http-out"
        | EffectCategory.Network -> "network"
        | EffectCategory.Database -> "database"
        | EffectCategory.FilesystemRead -> "filesystem-read"
        | EffectCategory.FilesystemWrite -> "filesystem-write"
        | EffectCategory.Process -> "process"
        | EffectCategory.Native -> "native"
        | EffectCategory.Environment -> "environment"
        | EffectCategory.Nondeterminism -> "nondeterminism"

    let tryParse (text: string) = all |> List.tryFind (fun c -> name c = text)

    /// Categories the website hides unless asked (plan section 4.2).
    let hiddenByDefault = [ EffectCategory.Environment; EffectCategory.Nondeterminism ]
