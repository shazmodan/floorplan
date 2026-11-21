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
      Location: Location option
      /// The types a namespace unit declares, by simple name, sorted; empty for other kinds.
      Types: string list }

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
    /// Inbound only: work the host starts by itself, not on a request (a hosted service).
    | Background

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
      /// The project boxes of UnitGraph; node ids are project ids. Projects whose units are all unconnected have
      /// no box here, only in DetachedBoxes.
      ProjectBoxes: NodeLayout list
      /// Dependencies between units of different projects, drawn once per pair of projects, between their boxes.
      ProjectEdges: EdgeLayout list
      /// Units with no dependency in either direction, below the graph: one box per project; ids are project ids.
      DetachedBoxes: NodeLayout list
      /// Where requests enter and side-effects leave: the units that handle entry points or have exit points, the
      /// units between them, and cards listing the entry and exit points. Node ids are "unit:<unit id>",
      /// "entries:<unit id>" and "exits:<unit id>|<category>".
      FlowGraph: GraphLayout }

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

/// What a C4 element is (https://c4model.com): the four levels' kinds, and the people and systems around them.
[<RequireQualifiedAccess>]
type C4Kind =
    | Person
    | SoftwareSystem
    | ExternalSystem
    | Container
    | Component
    | Code

/// An element of the C4 model, derived from the analysis: what it was derived from is in `Sources`.
type C4Element =
    { Id: string
      Kind: C4Kind
      Name: string
      /// What it is built with or of, e.g. "C# · net8.0 · web application".
      Technology: string option
      Description: string
      /// The element it is part of: a container's system, a component's container, a code element's component.
      Parent: string option
      Tags: string list
      /// Ids of the projects, units, entry points and exit points it was derived from.
      Sources: string list }

/// A relationship between the most specific elements it holds between; a view shows it between the elements it
/// shows that contain them.
type C4Relationship =
    { From: string
      To: string
      /// What happens, e.g. "GET /api/orders", "sends to orders".
      Description: string
      /// How, e.g. "HTTP", "RabbitMQ.Client".
      Technology: string option
      /// Ids of the entry points, exit points and dependencies behind it.
      Sources: string list }

[<RequireQualifiedAccess>]
type C4Level =
    | Context
    | Containers
    | Components
    | Code

/// One diagram: a level of the model, of one element (the system, a container, a component).
type C4View =
    { Id: string
      Level: C4Level
      /// The element the view is of.
      Scope: string
      /// The elements it shows.
      Elements: string list
      /// The model's relationships between the elements shown: each between the shown elements that contain its
      /// ends, those with the same ends and description merged.
      Relationships: C4Relationship list
      Layout: GraphLayout }

/// The C4 model of the solution, in four levels (docs/c4-plan.md).
type C4Model =
    { Elements: C4Element list
      Relationships: C4Relationship list
      Views: C4View list }

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
      /// The C4 model of the solution, derived from everything above.
      Architecture: C4Model
      Diagnostics: Diagnostic list }

module AnalysisModel =
    /// 2: the C4 model (Architecture).
    [<Literal>]
    let CurrentSchemaVersion = 2

    let emptyArchitecture =
        { Elements = []
          Relationships = []
          Views = [] }

    let emptyGraph =
        { Width = 0.0
          Height = 0.0
          Nodes = []
          Edges = [] }

    let emptyLayout =
        { ProjectGraph = emptyGraph
          UnitGraph = emptyGraph
          ProjectBoxes = []
          ProjectEdges = []
          DetachedBoxes = []
          FlowGraph = emptyGraph }

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
          Architecture = emptyArchitecture
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
          EffectCategory.Nondeterminism
          EffectCategory.Background ]

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
        | EffectCategory.Background -> "background"

    let tryParse (text: string) = all |> List.tryFind (fun c -> name c = text)

    /// Categories the website hides unless asked (plan section 4.2).
    let hiddenByDefault = [ EffectCategory.Environment; EffectCategory.Nondeterminism ]
