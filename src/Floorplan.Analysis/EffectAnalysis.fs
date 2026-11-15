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

/// One rule a method reaches: the first call on the shortest path, and the method that call leads to.
type CachedWitness =
    { RuleId: string
      Category: EffectCategory
      Primitive: string
      PrimitiveAssembly: string
      Distance: int
      Step: Summaries.PathStep
      /// The key of the next method on the path; none when Step is the call to the primitive.
      Next: string option }

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
    Caching.hash [ "summaries-v2"; assembly; mvid; catalog ]

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

    let buildGraph () =
        let g = CallGraph(set, recognizers, cachedAssemblies.Contains)

        g.AddRoots(
            users ()
            |> Seq.collect (fun (_, a) -> a.Types)
            |> Seq.collect (fun t -> t.Methods)
            |> Seq.filter (fun m -> not m.IsAbstract)
            |> Seq.map (fun m -> m.Ref)
        )

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
                |> Seq.exists (fun e ->
                    cachedAssemblies.Contains e.Target.Type.Assembly && not (analyzed.Contains e.Target.Key))

            if miss then
                cached.Clear()
                cachedAssemblies.Clear()
                (set :> IDisposable).Dispose()
                set <- newSet ()
                buildGraph ()
            else
                g

    let userList = users ()
    let matchers = Dictionary<string, Matcher>()
    let summaries = Dictionary<string, Summaries.Summaries>()

    member _.Set = set
    member _.Graph = graph
    member _.Users = userList
    member _.Recognizers = recognizers
    member _.Attributor = Attribution.Attributor(input.Units, input.Projects)

    /// Dependency assemblies whose summaries came from the cache.
    member _.CachedAssemblies = List.ofSeq cachedAssemblies

    member _.CatalogFor(project: AnalyzedProject) = catalogFor project

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
            let s =
                Summaries.Summaries(graph, matcher, fun m ->
                    set.Origin m.Type.Assembly = Dependency && not (cachedAssemblies.Contains m.Type.Assembly))

            summaries[matcher.Catalog.Name] <- s
            s

    /// Rules a dependency method reaches under a project's catalog: from the cache or from the call graph.
    member this.EffectsFor(project: AnalyzedProject) : MethodRef -> Summaries.DependencyEffect list =
        let catalog = (this.CatalogFor project).Catalog.Name
        let computed = this.SummariesFor project
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

        fun (m: MethodRef) ->
            match table m.Type.Assembly with
            | Some t ->
                match t.TryGetValue m.Key with
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

                        { RuleId = w.RuleId
                          Category = w.Category
                          Primitive = w.Primitive
                          PrimitiveAssembly = w.PrimitiveAssembly
                          Steps = path w [] 0 })
                | _ -> []
            | None -> computed.Effects m

    /// Writes summaries of the dependencies analyzed in this run, for later runs.
    member this.SaveCache() =
        if store.Enabled then
            let all = graph.ReachableMethods
            let reachable = all |> List.filter (fun m -> set.Origin m.Type.Assembly = Dependency)
            let byAssembly = reachable |> List.groupBy (fun m -> m.Type.Assembly)

            // Calls from anywhere to virtual methods declared in a dependency, resolved to implementations.
            let virtualTargets =
                all
                |> List.collect graph.Callees
                |> List.filter (fun e -> e.Kind = Virtual && set.Origin e.Declared.Type.Assembly = Dependency)
                |> List.groupBy (fun e -> e.Declared)

            let toCached (w: Summaries.Witness) : CachedWitness =
                { RuleId = w.Hit.Rule.Id
                  Category = w.Hit.Category
                  Primitive = w.Hit.Primitive.Display
                  PrimitiveAssembly = w.Hit.Primitive.Type.Assembly
                  Distance = w.Distance
                  Step = Summaries.toStep w.Edge
                  Next = if w.Distance = 1 then None else Some w.Edge.Target.Key }

            for project in input.Projects |> List.distinctBy (fun p -> (catalogFor p).Catalog.Name) do
                let catalog = (catalogFor project).Catalog.Name
                let summaries = this.SummariesFor project

                for (assembly, methods) in byAssembly do
                    if not (cachedAssemblies.Contains assembly) then
                        let direct =
                            methods |> List.map (fun m -> { Key = m.Key; Witnesses = summaries.Reaches m |> List.map toCached })

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
                                            { toCached w with
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
                            (direct @ declared) |> List.filter (fun m -> not m.Witnesses.IsEmpty) |> List.distinctBy (fun m -> m.Key)

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
        |> List.map (fun (project, assembly) ->
            let ctx: ExitPoints.ProjectContext =
                { Project = project
                  Set = program.Set
                  Graph = program.Graph
                  Matcher = program.MatcherFor project
                  Effects = program.EffectsFor project
                  Attributor = attributor
                  Recognizers = program.Recognizers }

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
      Diagnostics = fallback @ missing @ (perProject |> List.collect snd |> List.distinct)
      Catalogs = catalogs }
