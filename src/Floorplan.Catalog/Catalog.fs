/// The boundary catalog (plan section 4): the tool's entire knowledge of what counts as "outside the
/// application". It is data, not code.
namespace Floorplan.Catalog

open Floorplan.Model

type RuleKind =
    /// An effect happens here.
    | Sink
    /// External data enters here.
    | Source
    /// A handler is registered here.
    | Registration
    /// The analysis cannot see past this call: reflection, run-time code generation, `dynamic` (plan risk 7).
    | BlindSpot

/// Which argument of a matched call carries something worth showing: a URL, SQL text, a path, a route template,
/// a handler. Argument 0 is the first declared parameter; for instance methods `this` is not counted.
type PayloadSpec = { Argument: int; Role: string }

type Rule =
    { Id: string
      /// Informational: the assembly that defines the type. Matching uses the type's full name, because call
      /// sites often name a facade assembly.
      Assembly: string
      Type: string
      /// Member names. "*" matches all, a trailing "*" matches a prefix, ".ctor" is a constructor.
      Members: string list
      /// The rule also applies to overrides and implementations in derived types.
      IncludeDerivedTypes: bool
      Kind: RuleKind
      Category: EffectCategory option
      Payload: PayloadSpec list
      /// For registrations that imply a method, e.g. MapGet.
      HttpMethod: string option
      /// For request sources: which part of the request the value is (plan section 6.3).
      RequestPart: RequestPart option
      /// Only overloads whose first parameter has this type, e.g. "System.String" for path-taking constructors.
      WhenFirstParameter: string option
      /// For blind spots: what kind of blind spot.
      BlindSpot: string option
      /// The catalog that defined the rule.
      Source: string }

type Catalog =
    { /// The catalog name, which is the target framework it describes, e.g. "net8.0". Combined catalogs join names.
      Name: string
      Rules: Rule list }

/// The catalog chosen for a project (plan sections 3.2 and 8.2).
type CatalogChoice =
    { Catalog: Catalog
      /// True when the project targets a newer .NET than any catalog describes.
      Fallback: bool }
