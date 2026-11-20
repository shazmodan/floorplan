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
    /// A platform function that compares request data with a known value, e.g. HttpMethods.IsGet.
    | Comparison
    /// The platform calls these members on instances of derived types it is given, e.g. EndpointDataSource.Endpoints.
    | Callback
    /// An object that attaches information to an endpoint, e.g. the HTTP methods it accepts.
    | Metadata
    /// A static field of the platform with a known value, e.g. HttpMethods.Get = "GET" (ComparesTo holds it).
    | Constant
    /// A log call, e.g. Serilog's ILogger.Information. What the logger's sinks do is the logging configuration's
    /// effect, the same at every call: it is reported once for each unit that logs, not at every call.
    | Logger
    /// A query that runs on the database when it is enumerated, e.g. Entity Framework Core's DbSet<T>. With
    /// members "*", every value of the type is one; otherwise the results of calls to the members are, with the
    /// SQL they run in `Payload` (FromSqlRaw).
    | Query
    /// Objects a library makes by reflection from an XML configuration file, e.g. NLog's targets: each type its
    /// `Configuration` names in a configuration file in the output folder is constructed, as if in code.
    | Configured

/// How the log calls of a logger rule are made.
[<RequireQualifiedAccess>]
type LogCall =
    /// A call to one of the members logs.
    | Direct
    /// The members make a delegate that logs when it is invoked (LoggerMessage.Define): invoking it is the log call.
    | Delegate
    /// The rule's type is an attribute: a call to a method it marks logs ([LoggerMessage], whose body a source
    /// generator writes). Members are not used.
    | Attribute

/// Where a configured rule's types are named in an XML configuration file: in elements named `Elements` under
/// the element `Root` (the document's root, or a child of a `<configuration>` root as in app.config), in their
/// attribute `TypeAttribute` (of any namespace, so `xsi:type` too). A value is a type's full name, optionally
/// assembly-qualified, or else an alias: the first argument of the attribute `Alias` on a type assignable to the
/// rule's type, compared ignoring case.
type ConfigurationSpec =
    { Root: string
      Elements: string list
      TypeAttribute: string
      Alias: string option }

/// Which argument of a matched call carries something worth showing: a URL, SQL text, a path, a route template,
/// a handler. Argument 0 is the first declared parameter; for instance methods `this` is not counted.
/// With `Property`, the payload is a property of the receiver instead (a command's SQL text), or of argument
/// `Argument` when both are given (a request's URL): the value last stored through its setter before the call, or
/// else the constructor argument at `ConstructorArgument` when the object was created with a constructor whose
/// parameter there is a string or a `Uri`. Without an argument, `Argument` is -1.
type PayloadSpec =
    { Argument: int
      Role: string
      Property: string option
      ConstructorArgument: int option }

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
      /// For comparisons: the value the request part is compared with.
      ComparesTo: string option
      /// For loggers whose library hands each call on to the loggers its providers make, e.g.
      /// Microsoft.Extensions.Logging: the interface member those loggers implement ("Type::Member"). A log
      /// call reaches what every constructed implementation of it reaches; callback rules make them reachable.
      Dispatch: string option
      /// For loggers: how a log call is made.
      LogCall: LogCall
      /// For configured rules: where the configuration names the types.
      Configuration: ConfigurationSpec option
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
