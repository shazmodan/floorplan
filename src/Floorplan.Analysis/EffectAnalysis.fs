/// Effect analysis (plan section 3.3, stage 4): builds one call graph over every user assembly and its
/// dependencies, matches it against the boundary catalog of each project's target framework, and returns
/// exit points and diagnostics.
module Floorplan.Analysis.EffectAnalysis

open System
open System.Collections.Generic
open Floorplan.Model
open Floorplan.IL
open Floorplan.Catalog

type EffectResult =
    { ExitPoints: ExitPoint list
      Diagnostics: Diagnostic list
      /// The catalog used for each project, by project id.
      Catalogs: Map<string, CatalogChoice> }

/// A segment of a payload origin (`Values.Segment`): text, an argument (with a member path), or unknown when
/// neither is set.
type CachedSegment =
    { Text: string option
      Parameter: int option
      Members: string list }

type CachedPayload =
    { Role: string
      Segments: CachedSegment list }

module private CachedPayload =
    let ofPayload (p: Summaries.PayloadOrigin) : CachedPayload =
        { Role = p.Role
          Segments =
            p.Origin
            |> List.map (function
                | Values.Literal t -> { Text = Some t; Parameter = None; Members = [] }
                | Values.Unknown -> { Text = None; Parameter = None; Members = [] }
                | Values.FromParameter(i, members) -> { Text = None; Parameter = Some i; Members = members }) }

    let toPayload (p: CachedPayload) : Summaries.PayloadOrigin =
        { Role = p.Role
          Origin =
            p.Segments
            |> List.map (fun s ->
                match s.Text, s.Parameter with
                | Some t, _ -> Values.Literal t
                | None, Some i -> Values.FromParameter(i, s.Members)
                | None, None -> Values.Unknown) }

/// One rule a method reaches: the first call on the shortest path, and the method that call leads to.
type CachedWitness =
    { RuleId: string
      Category: EffectCategory
      Primitive: string
      PrimitiveAssembly: string
      Distance: int
      Step: Summaries.PathStep
      /// The key of the next method on the path; none when Step is the call to the primitive.
      Next: string option
      /// The primitive's payload in terms of the method's arguments; none when the rule has no payload.
      Payload: CachedPayload option }

type CachedMethod =
    { Key: string
      Witnesses: CachedWitness list }

type AssemblyIdentity = { Name: string; Mvid: string }

/// A dependency assembly's summaries for one catalog, as cached on disk (plan section 5, step 2). Valid while
/// the assembly and every assembly its evidence paths pass through keep their MVIDs.
type CachedAssembly =
    { Assembly: string
      Mvid: string
      Catalog: string
      Depends: AssemblyIdentity list
      /// Methods that were analyzed; a call into any other method of the assembly needs a fresh analysis.
      Analyzed: string list
      Methods: CachedMethod list }

let private assemblyOfKey (key: string) =
    if key.StartsWith "[" then key.Substring(1, key.IndexOf ']' - 1) else ""

let private mvidOf (path: string) =
    try
        use stream = IO.File.OpenRead path
        use pe = new Reflection.PortableExecutable.PEReader(stream)
        let reader = Reflection.Metadata.PEReaderExtensions.GetMetadataReader pe
        string (reader.GetGuid(reader.GetModuleDefinition().Mvid))
    with _ ->
        ""

let private cacheKey (assembly: string) (mvid: string) (catalog: string) =
    Caching.hash [ "summaries-v19"; assembly; mvid; catalog ]

/// The cache key under which an assembly keeps what a callback member reaches through its implementations there.
let private callbackKey (assembly: string) (callback: string) = $"[{assembly}]callback:{callback}"

/// The program every analysis of one run shares: assemblies, call graph, per-catalog matchers and summaries,
/// and the dependency summary cache.
type Program(input: AnalysisInput, recognizers: ICodegenRecognizer list, store: Caching.Store) =
    let newSet () =
        new AssemblySet(
            CecilReader.create (),
            input.Projects |> List.map (fun p -> p.ProjectId, p.AssemblyPath),
            input.Projects |> List.collect (fun p -> p.SearchPaths) |> List.distinct
        )

    let extras = input.ExtraCatalogs |> List.map CatalogLoader.loadFile

    let catalogFor (project: AnalyzedProject) =
        let choice = CatalogLoader.forTargetFramework project.TargetFramework
        { choice with Catalog = CatalogLoader.extend choice.Catalog extras }

    let catalogNames =
        input.Projects |> List.map (fun p -> (catalogFor p).Catalog.Name) |> List.distinct

    // Callback members of every catalog in use: one call graph serves them all.
    let callbacks =
        input.Projects
        |> List.collect (fun p -> (catalogFor p).Catalog.Rules)
        |> List.filter (fun r -> r.Kind = Callback)
        |> List.map (fun r -> r.Type, r.Members)
        |> List.distinct

    do Progress.report $"Reading the assemblies of {input.Projects.Length} projects and their dependencies"
    let mutable set = newSet ()
    let mvids = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

    let mvid (assembly: string) =
        match mvids.TryGetValue assembly with
        | true, m -> m
        | _ ->
            let m =
                match set.PathOf assembly with
                | Some p -> mvidOf p
                | None -> ""

            mvids[assembly] <- m
            m

    // Cached summaries by (assembly, catalog), for dependencies whose cache is valid for every catalog in use.
    let cached = Dictionary<struct (string * string), CachedAssembly>()

    do
        if store.Enabled then
            Progress.report "Checking the summary cache for the dependencies"

            for name in set.KnownAssemblies do
                if set.Origin name = Dependency then
                    let entries =
                        catalogNames
                        |> List.map (fun c -> c, store.TryRead<CachedAssembly>("summaries", cacheKey name (mvid name) c))

                    let valid =
                        entries
                        |> List.forall (fun (_, e) ->
                            match e with
                            | Some e -> e.Mvid <> "" && e.Depends |> List.forall (fun d -> mvid d.Name = d.Mvid)
                            | None -> false)

                    if valid then
                        for (c, e) in entries do
                            cached[struct (name, c)] <- e.Value

    let cachedAssemblies =
        HashSet<string>(cached.Keys |> Seq.map (fun (struct (a, _)) -> a), StringComparer.OrdinalIgnoreCase)

    let users () =
        input.Projects
        |> List.choose (fun p ->
            set.TryLoad(IO.Path.GetFileNameWithoutExtension p.AssemblyPath) |> Option.map (fun a -> p, a))

    // Configured rules of every catalog in use, and the output folders whose configuration files name types.
    let configuredRules =
        input.Projects
        |> List.collect (fun p -> (catalogFor p).Catalog.Rules)
        |> List.filter (fun r -> r.Kind = Configured)

    let outputFolders =
        input.Projects |> List.map (fun p -> IO.Path.GetDirectoryName p.AssemblyPath)

    let findConfigured () =
        Configured.Resolver(set).Find(configuredRules, outputFolders)

    do Progress.report "Reading the configuration files in the output folders"
    let mutable configured = findConfigured ()

    let buildGraph () =
        Progress.report "Building the call graph"

        let g = CallGraph(set, recognizers, cachedAssemblies.Contains, callbacks)

        g.AddRoots(
            users ()
            |> Seq.collect (fun (_, a) -> a.Types)
            |> Seq.collect (fun t -> t.Methods)
            |> Seq.filter (fun m -> not m.IsAbstract)
            |> Seq.map (fun m -> m.Ref)
        )

        // A configured type is made for the project whose output folder holds the configuration file.
        let madeFor (file: string) =
            let folder = IO.Path.GetFullPath(IO.Path.GetDirectoryName file)

            input.Projects
            |> List.tryFind (fun p ->
                String.Equals(IO.Path.GetFullPath(IO.Path.GetDirectoryName p.AssemblyPath), folder, StringComparison.OrdinalIgnoreCase))
            |> Option.map (fun p -> IO.Path.GetFileNameWithoutExtension p.AssemblyPath)
            |> Option.defaultValue "*"

        for (by, cs) in fst configured |> List.groupBy (fun c -> madeFor c.File) do
            g.AddConstructed(cs |> List.choose (fun c -> match c.Made with Configured.Constructed t -> Some t | _ -> None), by)

        g.AddRoots(fst configured |> List.choose (fun c -> match c.Made with Configured.Called m -> Some m | _ -> None))
        g

    let graph =
        let g = buildGraph ()

        if cachedAssemblies.Count = 0 then
            g
        else
            // A call into a cached assembly outside what the cache covers: analyze everything afresh.
            let analyzed =
                HashSet<string>(cached.Values |> Seq.collect (fun e -> e.Analyzed))

            let miss =
                g.ReachableMethods
                |> Seq.collect g.Callees
                |> Seq.append g.CallbackEdges
                |> Seq.exists (fun e ->
                    cachedAssemblies.Contains e.Target.Type.Assembly && not (analyzed.Contains e.Target.Key))

            // A type configured now that the cached run did not construct, or a method it did not call: what
            // reaches it is not in the cache.
            let configuredMiss =
                fst configured
                |> List.exists (fun c ->
                    match c.Made with
                    | Configured.Constructed t ->
                        cachedAssemblies.Contains t.Assembly
                        && match set.TryType t with
                           | Some(def, _) ->
                               def.Methods
                               |> List.exists (fun m ->
                                   m.IsConstructor && not m.IsStatic && m.Ref.ParameterCount = 0 && not (analyzed.Contains m.Ref.Key))
                           | None -> false
                    | Configured.Called m -> cachedAssemblies.Contains m.Type.Assembly && not (analyzed.Contains m.Key))

            if miss || configuredMiss then
                Progress.report "The summary cache does not cover this solution's calls: analyzing every dependency afresh"
                cached.Clear()
                cachedAssemblies.Clear()
                (set :> IDisposable).Dispose()
                set <- newSet ()
                configured <- findConfigured ()
                buildGraph ()
            else
                g

    do Progress.report $"Call graph: {graph.ReachableMethods.Length} methods reachable"
    let userList = users ()
    let producers = Values.Producers(set, recognizers)
    let matchers = Dictionary<string, Matcher>()
    let summaries = Dictionary<string, Summaries.Summaries>()

    // Each project's reference closure: its own assembly and every assembly it references, directly or not.
    let closures = Dictionary<string, HashSet<string>>()

    let closureOf (project: AnalyzedProject) =
        match closures.TryGetValue project.ProjectId with
        | true, c -> c
        | _ ->
            let seen = HashSet<string>(StringComparer.OrdinalIgnoreCase)
            let pending = Stack<string>()
            pending.Push(IO.Path.GetFileNameWithoutExtension project.AssemblyPath)

            while pending.Count > 0 do
                let name = pending.Pop()

                if seen.Add name then
                    match set.TryLoad name with
                    | Some a ->
                        for r in a.ReferencedAssemblies do
                            if not (seen.Contains r) then
                                pending.Push r
                    | None -> ()

            closures[project.ProjectId] <- seen
            seen

    let graphs = Dictionary<string, CallGraph>()
    let settings = Dictionary<string, Values.Settings>()

    member _.Set = set

    /// The call graph of the whole solution, over which dependency summaries are computed.
    member _.Graph = graph

    /// The call graph as a project sees it: its virtual calls reach only implementations in types that code in
    /// its reference closure constructs.
    member _.GraphFor(project: AnalyzedProject) =
        match graphs.TryGetValue project.ProjectId with
        | true, g -> g
        | _ ->
            let g = graph.ForAssemblies (closureOf project).Contains
            graphs[project.ProjectId] <- g
            g

    /// What the configuration files name, for people: an info diagnostic for each type constructed, and a warning
    /// for each value that names no type the analysis can read.
    member _.ConfiguredDiagnostics: Diagnostic list =
        (fst configured
         |> List.map (fun c ->
             { Severity = DiagnosticSeverity.Info
               Code = "configured.type"
               Message =
                 match c.Made with
                 | Configured.Constructed t ->
                     $"{IO.Path.GetFileName c.File} names \"{c.Named}\": {t.FullName} is analyzed as constructed (rule {c.Rule.Id})."
                 | Configured.Called m ->
                     $"{IO.Path.GetFileName c.File} names \"{c.Named}\": {m.Display} is analyzed as called (rule {c.Rule.Id})."
               ProjectId = None
               Location = None })
         |> List.distinct)
        @ snd configured

    /// What a call to a member that loads one of a project's configuration files makes, by the member called:
    /// for each object the file names, a description ("nlog.config names \"WebService\"") and the method that
    /// makes it, a type's parameterless constructor or a method called by name.
    member _.LoadedFor(project: AnalyzedProject) : MethodRef -> (string * MethodRef) list =
        let folder = IO.Path.GetFullPath(IO.Path.GetDirectoryName project.AssemblyPath)

        let mine =
            fst configured
            |> List.filter (fun c ->
                String.Equals(IO.Path.GetFullPath(IO.Path.GetDirectoryName c.File), folder, StringComparison.OrdinalIgnoreCase))

        let matches (declared: MethodRef) (pattern: string) =
            match pattern.LastIndexOf "::" with
            | -1 -> false
            | i ->
                let typeName = pattern.Substring(0, i)
                let name = pattern.Substring(i + 2)

                declared.Type.FullName = typeName
                && (if name.EndsWith "*" then declared.Name.StartsWith(name.TrimEnd '*') else declared.Name = name)

        let makers (c: Configured.ConfiguredType) =
            match c.Made with
            | Configured.Constructed t ->
                match set.TryType t with
                | Some(def, _) ->
                    def.Methods
                    |> List.filter (fun m -> m.IsConstructor && not m.IsStatic && m.Ref.ParameterCount = 0)
                    |> List.map (fun m -> m.Ref)
                | None -> []
            | Configured.Called m -> [ m ]

        fun (declared: MethodRef) ->
            mine
            |> List.filter (fun c -> c.LoadedBy |> List.exists (matches declared))
            |> List.collect (fun c ->
                makers c |> List.map (fun m -> $"{IO.Path.GetFileName c.File} names \"{c.Named}\"", m))
    member _.Users = userList
    member _.Recognizers = recognizers
    member _.Producers = producers
    member _.Attributor = Attribution.Attributor(input.Units, input.Projects)

    /// Dependency assemblies whose summaries came from the cache.
    member _.CachedAssemblies = List.ofSeq cachedAssemblies

    member _.CatalogFor(project: AnalyzedProject) = catalogFor project

    /// A project's configuration: the settings its output folder's appsettings.json gives, and the options
    /// types code in its reference closure binds to a section of them.
    member this.SettingsFor(project: AnalyzedProject) : Values.Settings =
        match settings.TryGetValue project.ProjectId with
        | true, s -> s
        | _ ->
            let matcher: Matcher = this.MatcherFor project
            let values = Configured.appSettings (IO.Path.GetDirectoryName project.AssemblyPath)

            let unbound: Values.Settings =
                { Read = matcher.Setting
                  Value =
                    fun k ->
                        match values.TryGetValue k with
                        | true, v -> Some v
                        | _ -> None
                  Bound = fun _ -> None
                  SectionTypes =
                    matcher.Catalog.Rules
                    |> List.filter (fun r -> r.Kind = Setting && r.Setting |> Option.exists (fun s -> s.Reads = SettingRead.Own))
                    |> List.map (fun r -> r.Type)
                    |> Set.ofList }

            // Configure<T>(section), AddOptions<T>().Bind(section) and BindConfiguration(path), with a constant
            // section: an options type bound to two sections is bound to neither.
            let closure = closureOf project

            let binds =
                [ for (_, a) in userList do
                      if closure.Contains a.Name then
                          for t in a.Types do
                              for m in t.Methods do
                                  match m.Body.Value with
                                  | Some body ->
                                      let calls =
                                          body.Instructions
                                          |> Array.choose (fun i ->
                                              match i.Op with
                                              | Call r
                                              | CallVirt r when not r.TypeArguments.IsEmpty ->
                                                  match matcher.Setting r with
                                                  | Some spec when spec.Reads = SettingRead.BindSection || spec.Reads = SettingRead.BindPath ->
                                                      Some(i.Offset, r, spec)
                                                  | _ -> None
                                              | _ -> None)

                                      if calls.Length > 0 then
                                          match DataFlow.ofMethod m with
                                          | Some flow ->
                                              let evaluator =
                                                  Values.Evaluator(flow, recognizers, Values.constantReturn set, (fun m n -> producers.Member(m, n)), unbound)

                                              for (offset, r, spec) in calls do
                                                  let index = if r.HasThis then spec.Argument + 1 else spec.Argument

                                                  let section =
                                                      if spec.Reads = SettingRead.BindPath then
                                                          match evaluator.Operand(offset, index) with
                                                          | Values.Text path -> Some path
                                                          | _ -> None
                                                      else
                                                          evaluator.Section(offset, index)

                                                  yield r.TypeArguments.Head.FullName, section
                                          | None -> ()
                                  | None -> () ]
                |> List.groupBy fst
                |> List.choose (fun (t, sections) ->
                    match sections |> List.map snd |> List.distinct with
                    | [ Some s ] -> Some(t, s)
                    | _ -> None)
                |> Map.ofList

            let s = { unbound with Bound = fun t -> Map.tryFind t binds }
            settings[project.ProjectId] <- s
            s

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
            Progress.report $"Summarizing what dependency methods reach (catalog {matcher.Catalog.Name})"

            let s =
                Summaries.Summaries(graph, matcher, recognizers, producers, fun m ->
                    set.Origin m.Type.Assembly = Dependency && not (cachedAssemblies.Contains m.Type.Assembly))

            summaries[matcher.Catalog.Name] <- s
            s

    /// Rules a cached method reaches under a catalog, by assembly and key: None when the assembly is not cached.
    member private this.CachedEffects(catalog: string) : string -> string -> Summaries.DependencyEffect list option =
        let tables = Dictionary<string, Dictionary<string, CachedWitness list>>(StringComparer.OrdinalIgnoreCase)

        let table (assembly: string) =
            match tables.TryGetValue assembly with
            | true, t -> Some t
            | _ ->
                match cached.TryGetValue(struct (assembly, catalog)) with
                | true, entry ->
                    let t = Dictionary<string, CachedWitness list>()

                    for m in entry.Methods do
                        t[m.Key] <- m.Witnesses

                    tables[assembly] <- t
                    Some t
                | _ -> None

        let witness (key: string) (ruleId: string) =
            table (assemblyOfKey key)
            |> Option.bind (fun t ->
                match t.TryGetValue key with
                | true, ws -> ws |> List.tryFind (fun w -> w.RuleId = ruleId)
                | _ -> None)

        fun (assembly: string) (key: string) ->
            match table assembly with
            | Some t ->
                match t.TryGetValue key with
                | true, ws ->
                    ws
                    |> List.map (fun w ->
                        // Follow the next hops through the cached tables to rebuild the path.
                        let rec path (w: CachedWitness) (acc: Summaries.PathStep list) (guard: int) =
                            match w.Next with
                            | Some next when guard < 128 ->
                                match witness next w.RuleId with
                                | Some n -> path n (w.Step :: acc) (guard + 1)
                                | None -> List.rev (w.Step :: acc)
                            | _ -> List.rev (w.Step :: acc)

                        ({ RuleId = w.RuleId
                           Category = w.Category
                           Primitive = w.Primitive
                           PrimitiveAssembly = w.PrimitiveAssembly
                           Steps = path w [] 0
                           Payload = w.Payload |> Option.map CachedPayload.toPayload }
                        : Summaries.DependencyEffect))
                    |> Some
                | _ -> Some []
            | None -> None

    /// Rules a dependency method reaches under a project's catalog: from the cache or from the call graph.
    member this.EffectsFor(project: AnalyzedProject) : MethodRef -> Summaries.DependencyEffect list =
        let computed = this.SummariesFor project
        let fromCache = this.CachedEffects (this.CatalogFor project).Catalog.Name

        fun (m: MethodRef) ->
            match fromCache m.Type.Assembly m.Key with
            | Some effects -> effects
            | None -> computed.Effects m

    /// What a callback member ("Type::Member") reaches through the implementations it is linked to, with the
    /// assembly of each implementation: the shortest route for each rule and assembly. Implementations in cached
    /// assemblies come from the cache, which also keeps those the cached code constructs.
    member this.DispatchFor(project: AnalyzedProject) : string -> (string * Summaries.DependencyEffect) list =
        let effects = this.EffectsFor project
        let fromCache = this.CachedEffects (this.CatalogFor project).Catalog.Name

        // Summaries serve every project of the solution, so cached implementations are constructed by any of them:
        // a project's log calls reach only those in assemblies it references, directly or not.
        let referenced = closureOf project
        let graph = this.GraphFor project

        fun (callback: string) ->
            let linked =
                graph.CallbackTargets callback
                |> List.filter (fun e -> set.Origin e.Target.Type.Assembly = Dependency && referenced.Contains e.Target.Type.Assembly)
                |> List.collect (fun e ->
                    let step: Summaries.PathStep =
                        { Caller = e.Declared.Display
                          CallerAssembly = e.Declared.Type.Assembly
                          Declared = e.Target.Display
                          Virtual = true }

                    effects e.Target
                    |> List.map (fun d -> e.Target.Type.Assembly, { d with Steps = step :: d.Steps }))

            let cachedOnly =
                cachedAssemblies
                |> Seq.toList
                |> List.filter referenced.Contains
                |> List.collect (fun a ->
                    fromCache a (callbackKey a callback) |> Option.defaultValue [] |> List.map (fun d -> a, d))

            linked @ cachedOnly
            |> List.groupBy (fun (a, d) -> a, d.RuleId)
            |> List.map (fun (_, ds) -> ds |> List.minBy (fun (_, d) -> d.Steps.Length))
            |> List.sortBy (fun (a, d) -> a, d.RuleId)

    /// Writes summaries of the dependencies analyzed in this run, for later runs.
    member this.SaveCache() =
        if store.Enabled then
            Progress.report "Saving dependency summaries to the cache"
            let all = graph.ReachableMethods
            let reachable = all |> List.filter (fun m -> set.Origin m.Type.Assembly = Dependency)
            let byAssembly = reachable |> List.groupBy (fun m -> m.Type.Assembly)

            // Calls from anywhere to virtual methods declared in a dependency, resolved to implementations.
            let virtualTargets =
                all
                |> List.collect graph.Callees
                |> List.filter (fun e -> e.Kind = Virtual && set.Origin e.Declared.Type.Assembly = Dependency)
                |> List.groupBy (fun e -> e.Declared)

            let toCached (summaries: Summaries.Summaries) (m: MethodRef) (w: Summaries.Witness) : CachedWitness =
                { RuleId = w.Hit.Rule.Id
                  Category = w.Hit.Category
                  Primitive = w.Hit.Primitive.Display
                  PrimitiveAssembly = w.Hit.Primitive.Type.Assembly
                  Distance = w.Distance
                  Step = Summaries.toStep w.Edge
                  Next = if w.Distance = 1 then None else Some w.Edge.Target.Key
                  Payload = summaries.PayloadOf(m, w.Hit.Rule.Id) |> Option.map CachedPayload.ofPayload }

            for project in input.Projects |> List.distinctBy (fun p -> (catalogFor p).Catalog.Name) do
                let catalog = (catalogFor project).Catalog.Name
                let summaries = this.SummariesFor project

                for (assembly, methods) in byAssembly do
                    if not (cachedAssemblies.Contains assembly) then
                        let direct =
                            methods |> List.map (fun m -> { Key = m.Key; Witnesses = summaries.Reaches m |> List.map (toCached summaries m) })

                        let declared =
                            virtualTargets
                            |> List.filter (fun (d, _) -> d.Type.Assembly = assembly)
                            |> List.map (fun (d, edges) ->
                                { Key = d.Key
                                  Witnesses =
                                    edges
                                    |> List.collect (fun e ->
                                        summaries.Reaches e.Target
                                        |> List.map (fun w ->
                                            // The declared method has the implementation's arguments.
                                            { toCached summaries e.Target w with
                                                Distance = w.Distance + 1
                                                Step =
                                                    { Caller = d.Display
                                                      CallerAssembly = d.Type.Assembly
                                                      Declared = e.Target.Display
                                                      Virtual = true }
                                                Next = Some e.Target.Key }))
                                    |> List.sortBy (fun w -> w.Distance)
                                    |> List.distinctBy (fun w -> w.RuleId) })

                        // Callback members linked to implementations here, which a later run may not see
                        // constructed: the cached code that constructs them is not explored again.
                        let callbacksHere =
                            graph.CallbackEdges
                            |> List.filter (fun e -> e.Target.Type.Assembly = assembly)
                            |> List.groupBy (fun e -> e.Declared)
                            |> List.map (fun (d, edges) ->
                                { Key = callbackKey assembly d.Display
                                  Witnesses =
                                    edges
                                    |> List.collect (fun e ->
                                        summaries.Reaches e.Target
                                        |> List.map (fun w ->
                                            { toCached summaries e.Target w with
                                                Distance = w.Distance + 1
                                                Step =
                                                    { Caller = d.Display
                                                      CallerAssembly = d.Type.Assembly
                                                      Declared = e.Target.Display
                                                      Virtual = true }
                                                Next = Some e.Target.Key }))
                                    |> List.sortBy (fun w -> w.Distance)
                                    |> List.distinctBy (fun w -> w.RuleId) })

                        let entries =
                            (direct @ declared @ callbacksHere) |> List.filter (fun m -> not m.Witnesses.IsEmpty) |> List.distinctBy (fun m -> m.Key)

                        let depends =
                            entries
                            |> List.collect (fun m -> m.Witnesses |> List.choose (fun w -> w.Next |> Option.map assemblyOfKey))
                            |> List.append [ assembly ]
                            |> List.distinct
                            |> List.filter (fun a -> set.Origin a = Dependency)
                            |> List.map (fun a -> { Name = a; Mvid = mvid a })

                        let analyzed =
                            (methods |> List.map (fun m -> m.Key)) @ (declared |> List.map (fun m -> m.Key)) |> List.distinct

                        store.Write(
                            "summaries",
                            cacheKey assembly (mvid assembly) catalog,
                            { Assembly = assembly
                              Mvid = mvid assembly
                              Catalog = catalog
                              Depends = depends
                              Analyzed = analyzed
                              Methods = entries }
                        )

    interface IDisposable with
        member _.Dispose() = (set :> IDisposable).Dispose()

let exitPoints (program: Program) (input: AnalysisInput) : EffectResult =
    let attributor = program.Attributor

    let perProject =
        program.Users
        |> List.mapi (fun i (project, assembly) ->
            Progress.report $"Exit points of {project.ProjectId} ({i + 1} of {program.Users.Length})"

            let ctx: ExitPoints.ProjectContext =
                { Project = project
                  Set = program.Set
                  Graph = program.GraphFor project
                  Matcher = program.MatcherFor project
                  Effects = program.EffectsFor project
                  Dispatch = program.DispatchFor project
                  Loaded = program.LoadedFor project
                  Attributor = attributor
                  Recognizers = program.Recognizers
                  Producers = program.Producers
                  Settings = program.SettingsFor project }

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
      Diagnostics = fallback @ missing @ program.ConfiguredDiagnostics @ (perProject |> List.collect snd |> List.distinct)
      Catalogs = catalogs }
