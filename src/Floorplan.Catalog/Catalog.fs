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
    /// A library's log of its own workings, written only when the application turns it on (Serilog's SelfLog,
    /// NLog's InternalLogger): a call to it in a library's code is not followed. Turning it on is the
    /// application's own call, and an exit point there.
    | SelfLog
    /// A call in a library's code that is not followed: what it does is reported where the application set it up,
    /// e.g. an OpenTelemetry exporter's Export, which the processors of every provider reach for every exporter
    /// constructed, and which is the effect of the call that added that exporter (AddOtlpExporter).
    | Unfollowed
    /// A call that reads the application's configuration (Microsoft.Extensions.Configuration's indexer): what it
    /// reads is in `Setting`, and its value is the one the configuration files in the output folder give.
    | Setting

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

/// Where an XML configuration file names a configured rule's types: in elements named `Elements` under the
/// element `Root` (the document's root, or a child of a `<configuration>` root as in app.config), in their
/// attribute `TypeAttribute` (of any namespace, so `xsi:type` too).
type XmlConfiguration =
    { Root: string
      Elements: string list
      TypeAttribute: string
      /// The members that load such a file ("Type::Member", the member optionally ending in `*`): what the
      /// objects it names do when they are made is reported at a call to one of them.
      LoadedBy: string list }

/// Where a JSON configuration file (appsettings.json) names a configured rule's types: under the top-level
/// property `Section`, in objects that are the value of a property named in `Properties`, or a value of such a
/// property's object (a map of names to objects), in their string property `TypeProperty`.
type JsonConfiguration =
    { Section: string
      Properties: string list
      TypeProperty: string
      /// The members that load the section, as for an XML configuration.
      LoadedBy: string list }

/// What a configured rule's configuration names.
[<RequireQualifiedAccess>]
type ConfiguredName =
    /// Types, each constructed.
    | Type
    /// Methods of the rule's type, or extension methods on it, by name ignoring case, each called: Serilog's sinks
    /// (`"WriteTo": [{ "Name": "File" }]` calls `WriteTo.File`).
    | Method

/// Where configuration files name a configured rule's types or methods. A type is named by its full name,
/// optionally assembly-qualified, or else by an alias: the first argument of the attribute `Alias` on a type
/// assignable to the rule's type, compared ignoring case.
type ConfigurationSpec =
    { Xml: XmlConfiguration option
      Json: JsonConfiguration option
      Alias: string option
      Names: ConfiguredName }

/// What a setting rule's call reads or makes.
[<RequireQualifiedAccess>]
type SettingRead =
    /// The value at the key `Argument` names, under the section the configuration is (the receiver, or the first
    /// argument of a static method).
    | Value
    /// The section at the key `Argument` names, under that section.
    | Section
    /// The value of the section itself.
    | Own
    /// An options object (IOptions<T>.Value): its properties are the values under the section its type is bound to.
    | Options
    /// Binds the options type that is the call's generic argument to the section `Argument` is.
    | BindSection
    /// Binds the options type that is the call's generic argument to the section at the path `Argument` names.
    | BindPath
    /// Makes a builder of the options of the call's generic argument under the name `Name` gives
    /// (AddOptions<T>(name)); a bind called on the builder binds options of that name.
    | OptionsBuilder

/// What a setting rule's call reads: `Argument` as in a payload (the first declared parameter is 0), and keys
/// read are under `Prefix` (GetConnectionString's are under "ConnectionStrings"). `Name`: the argument that names
/// the options read, bound or built (IOptionsMonitor<T>.Get(name), Configure<T>(name, section)); without it they
/// are the default options (the empty name), or for a bind on a builder, the builder's.
type SettingSpec =
    { Reads: SettingRead
      Argument: int
      Prefix: string option
      Name: int option }

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
      /// Only overloads whose parameter types start with these, e.g. one of several that take the same first type.
      WhenParameters: string list
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
      /// For sinks: the rule's type is an attribute, and a call to a method it marks is the effect (Refit's
      /// [Get("/users/{id}")] on an interface method). Payload arguments are the attribute's.
      MarkedCalls: bool
      /// For sinks with includeDerivedTypes: only calls to methods of interfaces the solution declares that derive
      /// from the rule's type (an Orleans grain interface), not the library's own.
      ApplicationInterfaces: bool
      /// For setting rules: what the call reads.
      Setting: SettingSpec option
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
