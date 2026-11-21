/// The C# structure analyzer: compiles each project with Roslyn and turns every symbol use into an edge from
/// the enclosing namespace to the type that declares the symbol (plan sections 3.5 and 10, Phase 7). A unit is
/// a namespace that declares types; types in no namespace form the project's global namespace unit.
namespace Floorplan.Structure.CSharp

open System
open System.IO
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.CSharp
open Microsoft.CodeAnalysis.CSharp.Syntax
open Floorplan.Model
open Floorplan.ProjectLoading
open Floorplan.Structure

module CSharpStructureAnalyzer =
    /// Shown for the namespace of types declared outside any namespace.
    let globalNamespaceLabel = "(global namespace)"

    let private isGenerated (file: string) =
        let sep = string Path.DirectorySeparatorChar
        file.Contains(sep + "obj" + sep) || file.EndsWith ".g.cs" || file.EndsWith ".designer.cs"

    let private toLocation (span: FileLinePositionSpan) =
        { File = span.Path
          StartLine = span.StartLinePosition.Line + 1
          StartColumn = span.StartLinePosition.Character
          EndLine = span.EndLinePosition.Line + 1
          EndColumn = span.EndLinePosition.Character }

    let private property (project: LoadedProject) (name: string) =
        project.Options.AllProperties |> Map.tryFind name |> Option.bind Seq.tryHead |> Option.defaultValue ""

    /// The full name of a namespace, "" for the global one.
    let private namespaceName (ns: INamespaceSymbol) =
        if isNull ns || ns.IsGlobalNamespace then "" else ns.ToDisplayString()

    /// The name an edge points to for a symbol: its outermost-to-innermost containing types, joined with dots.
    /// It is resolved to the innermost unit that contains it, whatever language the target project is in.
    let rec private ownerName (symbol: ISymbol) : string option =
        match symbol with
        | null -> None
        | :? INamespaceSymbol -> None
        | :? ITypeParameterSymbol -> None
        | :? INamedTypeSymbol as t when t.IsTupleType || t.IsAnonymousType -> None
        | :? INamedTypeSymbol as t ->
            let t = t.OriginalDefinition

            match t.ContainingType with
            | null ->
                let ns = namespaceName t.ContainingNamespace
                Some(if ns = "" then t.Name else ns + "." + t.Name)
            | outer -> ownerName outer |> Option.map (fun o -> o + "." + t.Name)
        | :? IMethodSymbol as m when m.MethodKind = MethodKind.LocalFunction || m.MethodKind = MethodKind.AnonymousFunction -> None
        | :? IMethodSymbol as m ->
            let m = if isNull m.ReducedFrom then m else m.ReducedFrom
            ownerName m.ContainingType
        | :? IPropertySymbol
        | :? IFieldSymbol
        | :? IEventSymbol -> ownerName symbol.ContainingType
        | _ -> None

    /// The outermost type a symbol is or is declared in, by its namespace and name: what a type dependency names.
    let rec private outermostType (symbol: ISymbol) : string option =
        match symbol with
        | null -> None
        | :? INamespaceSymbol -> None
        | :? ITypeParameterSymbol -> None
        | :? INamedTypeSymbol as t when t.IsTupleType || t.IsAnonymousType -> None
        | :? INamedTypeSymbol as t when isNull t.ContainingType ->
            let t = t.OriginalDefinition
            let ns = namespaceName t.ContainingNamespace
            Some(if ns = "" then t.Name else ns + "." + t.Name)
        | :? IMethodSymbol as m when not (isNull m.ReducedFrom) -> outermostType m.ReducedFrom
        | s -> outermostType s.ContainingType

    /// The namespace whose code a position is in.
    let private enclosingNamespace (model: SemanticModel) (position: int) =
        let rec up (s: ISymbol) =
            match s with
            | null -> ""
            | :? INamespaceSymbol as ns -> namespaceName ns
            | s -> up s.ContainingSymbol

        up (model.GetEnclosingSymbol position)

    let private parseOptions (project: LoadedProject) =
        let symbols =
            (property project "DefineConstants").Split([| ';'; ','; ' ' |], StringSplitOptions.RemoveEmptyEntries)

        CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.None, SourceCodeKind.Regular, symbols)

    /// Analyzer assemblies from one folder, in a context of their own: projects may target different .NET versions,
    /// whose generators have the same names and different versions. Roslyn itself is shared with the tool.
    type private AnalyzerContext(folder: string) =
        inherit System.Runtime.Loader.AssemblyLoadContext($"analyzers:{folder}")

        override this.Load(name: System.Reflection.AssemblyName) =
            let shared = name.Name.StartsWith "Microsoft.CodeAnalysis" || name.Name = "System.Collections.Immutable" || name.Name = "System.Reflection.Metadata"
            let local = Path.Combine(folder, name.Name + ".dll")

            if not shared && File.Exists local then this.LoadFromAssemblyPath local else null

    let private contexts = System.Collections.Concurrent.ConcurrentDictionary<string, AnalyzerContext>(StringComparer.OrdinalIgnoreCase)

    let private assemblyLoader =
        { new IAnalyzerAssemblyLoader with
            member _.AddDependencyLocation _ = ()

            member _.LoadFromPath path =
                let folder = Path.GetDirectoryName(Path.GetFullPath path)
                contexts.GetOrAdd(folder, fun f -> new AnalyzerContext(f)).LoadFromAssemblyPath(Path.GetFullPath path) }

    /// The analyzer assemblies the compiler is given (`/analyzer:`), which carry the project's source generators.
    let private analyzerPaths (project: LoadedProject) =
        project.Options.OtherOptions
        |> List.choose (fun o ->
            if o.StartsWith "/analyzer:" || o.StartsWith "-analyzer:" then Some(o.Substring 10)
            elif o.StartsWith "/a:" || o.StartsWith "-a:" then Some(o.Substring 3)
            else None)
        |> List.map (fun p -> p.Trim '"')
        |> List.filter File.Exists
        |> List.distinct

    /// The values of compiler options with a prefix (`/additionalfile:`), as existing paths.
    let private optionPaths (project: LoadedProject) (prefixes: string list) =
        project.Options.OtherOptions
        |> List.choose (fun o ->
            prefixes
            |> List.tryFind (fun p -> o.StartsWith("/" + p, StringComparison.OrdinalIgnoreCase) || o.StartsWith("-" + p, StringComparison.OrdinalIgnoreCase))
            |> Option.map (fun p -> o.Substring(p.Length + 1).Trim '"'))
        // Relative to the project, as the compiler runs there.
        |> List.map (fun p -> if Path.IsPathRooted p then p else Path.GetFullPath(Path.Combine(Path.GetDirectoryName project.Path, p)))
        |> List.filter File.Exists
        |> List.distinct

    /// A file the compiler is given for generators to read (a Razor component).
    type private FileText(path: string) =
        inherit AdditionalText()
        override _.Path = path
        override _.GetText(_) = Text.SourceText.From(File.ReadAllText path)

    type private ConfigOptions(values: Collections.Generic.IReadOnlyDictionary<string, string>) =
        inherit Diagnostics.AnalyzerConfigOptions()

        override _.TryGetValue(key: string, value: byref<string>) =
            match values.TryGetValue key with
            | true, v ->
                value <- v
                true
            | _ ->
                value <- null
                false

    let private normalizePath (p: string) = p.Replace('\\', '/').ToLowerInvariant()

    /// The options of the analyzer configuration files the compiler is given (the global configuration MSBuild
    /// writes, `*.GeneratedMSBuildEditorConfig.editorconfig`): properties outside any section apply everywhere, a
    /// section names the file its properties apply to.
    type private ConfigProvider(files: string list) =
        inherit Diagnostics.AnalyzerConfigOptionsProvider()

        let everywhere = Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        let byFile = Collections.Generic.Dictionary<string, Collections.Generic.Dictionary<string, string>>()

        do
            for file in files do
                let mutable section: Collections.Generic.Dictionary<string, string> = everywhere

                for raw in File.ReadAllLines file do
                    let line = raw.Trim()

                    if line = "" || line.StartsWith "#" || line.StartsWith ";" then
                        ()
                    elif line.StartsWith "[" && line.EndsWith "]" then
                        let key = normalizePath (line.Substring(1, line.Length - 2))

                        section <-
                            match byFile.TryGetValue key with
                            | true, d -> d
                            | _ ->
                                let d = Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                byFile[key] <- d
                                d
                    else
                        match line.IndexOf '=' with
                        | -1 -> ()
                        | i -> section[line.Substring(0, i).Trim()] <- line.Substring(i + 1).Trim()

        let forPath (path: string) =
            match byFile.TryGetValue(normalizePath path) with
            | true, d -> ConfigOptions(d) :> Diagnostics.AnalyzerConfigOptions
            | _ -> ConfigOptions(Collections.Generic.Dictionary<string, string>()) :> Diagnostics.AnalyzerConfigOptions

        override _.GlobalOptions = ConfigOptions(everywhere)
        override _.GetOptions(tree: SyntaxTree) = forPath tree.FilePath
        override _.GetOptions(text: AdditionalText) = forPath text.Path

    /// Runs the project's source generators (LibraryImport, JSON, regex, logging, Razor...), so code that calls what
    /// they generate binds. Generators are given what the compiler gives them: the additional files (Razor
    /// components and pages) and the analyzer configuration. Generated code is compiled but gives no units and no
    /// edges. A generator that cannot be loaded or fails is skipped.
    let private runGenerators (project: LoadedProject) (options: CSharpParseOptions) (compilation: CSharpCompilation) : CSharpCompilation =
        let generators =
            analyzerPaths project
            |> List.collect (fun path ->
                try
                    (Microsoft.CodeAnalysis.Diagnostics.AnalyzerFileReference(path, assemblyLoader)).GetGenerators(LanguageNames.CSharp)
                    |> List.ofSeq
                with _ ->
                    [])

        if generators.IsEmpty then
            compilation
        else
            try
                let additional = optionPaths project [ "additionalfile:" ] |> List.map (fun f -> FileText(f) :> AdditionalText)
                let configs = optionPaths project [ "analyzerconfig:" ]

                let driver =
                    CSharpGeneratorDriver.Create(generators, additional, options, ConfigProvider(configs))
                let _, updated, _ = driver.RunGeneratorsAndUpdateCompilation(compilation)
                updated :?> CSharpCompilation
            with _ ->
                compilation

    /// A Roslyn compilation of the project from its MSBuild evaluation: its compile items and resolved references,
    /// with the code its source generators add.
    let compilation (project: LoadedProject) =
        let options = parseOptions project

        let trees =
            project.SourceFiles
            |> List.filter (fun f -> f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && File.Exists f)
            |> List.map (fun f -> CSharpSyntaxTree.ParseText(File.ReadAllText f, options, f))

        let references =
            project.ReferencePaths
            |> List.filter File.Exists
            |> List.map (fun r -> MetadataReference.CreateFromFile r :> MetadataReference)

        let kind =
            if project.IsApplication then OutputKind.ConsoleApplication else OutputKind.DynamicallyLinkedLibrary

        let compilation =
            CSharpCompilation.Create(
                project.AssemblyName,
                trees,
                references,
                CSharpCompilationOptions(kind, nullableContextOptions = NullableContextOptions.Enable, allowUnsafe = true)
            )

        runGenerators project options compilation

    /// Each namespace that declares a type in the project's own source, with where it is first declared and the
    /// simple names of the types it declares (not nested ones).
    let private declaredNamespaces (trees: SyntaxTree list) =
        trees
        |> List.collect (fun tree ->
            let root = tree.GetRoot()

            let rec walk (prefix: string) (node: SyntaxNode) =
                [ for child in node.ChildNodes() do
                      match child with
                      | :? BaseNamespaceDeclarationSyntax as ns ->
                          let name = if prefix = "" then ns.Name.ToString() else prefix + "." + ns.Name.ToString()
                          let own = ns.Members |> Seq.exists (fun m -> not (m :? BaseNamespaceDeclarationSyntax))

                          if own then
                              yield name, Some(toLocation (ns.GetLocation().GetLineSpan())), None

                          yield! walk name ns
                      | :? BaseTypeDeclarationSyntax as t -> yield prefix, None, Some t.Identifier.Text
                      | :? DelegateDeclarationSyntax as d -> yield prefix, None, Some d.Identifier.Text
                      | :? GlobalStatementSyntax when prefix = "" -> yield "", None, None
                      | _ -> () ]

            walk "" root)
        |> List.groupBy (fun (name, _, _) -> name)
        |> List.map (fun (name, found) ->
            name,
            found |> List.tryPick (fun (_, location, _) -> location),
            found |> List.choose (fun (_, _, t) -> t) |> List.distinct |> List.sort)
        |> List.sortBy (fun (name, _, _) -> name)

    let private analyzeProject (index: SolutionIndex) (project: LoadedProject) =
        let compilation = compilation project

        // The project's own source: not generated files under obj/, nor what source generators add.
        let ownFiles = project.SourceFiles |> List.filter (fun f -> not (isGenerated f)) |> set
        let isOwn (tree: SyntaxTree) = ownFiles.Contains tree.FilePath
        let unitId = UnitId.create project.Id

        let namespaces = declaredNamespaces (compilation.SyntaxTrees |> Seq.filter isOwn |> List.ofSeq)

        let units =
            namespaces
            |> List.map (fun (name, location, types) ->
                { Id = unitId name
                  ProjectId = project.Id
                  Kind = UnitKind.Namespace
                  Name = name
                  DisplayName = if name = "" then globalNamespaceLabel else name.Split('.') |> Array.last
                  Parent = None
                  Location = location
                  Types = types })

        let errors =
            compilation.GetDiagnostics()
            |> Seq.filter (fun d -> d.Severity = Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            |> Seq.truncate 5
            |> Seq.map (fun d ->
                { Severity = Floorplan.Model.DiagnosticSeverity.Warning
                  Code = "structure.compile-error"
                  Message = $"{project.Name}: {d.GetMessage()}"
                  ProjectId = Some project.Id
                  Location = if d.Location.IsInSource then Some(toLocation (d.Location.GetLineSpan())) else None })
            |> List.ofSeq

        /// The unit id an edge points to, in the project that declares the symbol.
        let target (symbol: ISymbol) =
            match symbol.ContainingAssembly with
            | null -> None
            | assembly ->
                match SolutionIndex.tryProjectOfAssembly assembly.Name index with
                | None -> None
                | Some target when SolutionIndex.supportLevel target index = SupportLevel.Limited ->
                    Some(UnitId.forProject target.Id, target.Id)
                | Some target -> ownerName symbol |> Option.map (fun o -> UnitId.create target.Id o, target.Id)

        let edges =
            compilation.SyntaxTrees
            |> Seq.filter isOwn
            |> Seq.collect (fun tree ->
                let model = compilation.GetSemanticModel(tree, false)

                tree.GetRoot().DescendantNodes()
                |> Seq.filter (fun n ->
                    // Names (of types, members, attributes) and target-typed constructions; using directives name namespaces only.
                    (n :? SimpleNameSyntax || n :? ImplicitObjectCreationExpressionSyntax)
                    && isNull (n.FirstAncestorOrSelf<UsingDirectiveSyntax>()))
                |> Seq.choose (fun (n: SyntaxNode) ->
                    let info = model.GetSymbolInfo n

                    let symbol =
                        if not (isNull info.Symbol) then info.Symbol
                        else info.CandidateSymbols |> Seq.tryHead |> Option.toObj

                    match symbol with
                    | null -> None
                    | symbol ->
                        target symbol
                        |> Option.map (fun (t, targetProject) ->
                            let types =
                                match outermostType (model.GetEnclosingSymbol n.SpanStart), outermostType symbol with
                                | Some a, Some b -> Some(a, b)
                                | _ -> None

                            unitId (enclosingNamespace model n.SpanStart), t, targetProject, n.GetLocation().GetLineSpan(), types)))
            |> Array.ofSeq

        let typeEdges =
            edges
            |> Array.choose (fun (from, target, _, _, types) -> types |> Option.map (fun (a, b) -> from, a, target, b))
            |> Array.filter (fun (_, a, _, b) -> a <> b)
            |> Array.countBy id
            |> Array.map (fun ((from, a, target, b), n) ->
                { FromUnit = from
                  FromType = a
                  ToUnit = target
                  ToType = b
                  Weight = n })
            |> List.ofArray

        let edges =
            edges
            |> Seq.map (fun (from, target, project, span, _) -> from, target, project, span)
            |> Seq.filter (fun (from, target, _, _) -> from <> target)
            |> Seq.groupBy (fun (from, target, _, _) -> from, target)
            |> Seq.map (fun ((from, target), uses) ->
                let uses = Array.ofSeq uses
                let _, _, targetProject, _ = uses[0]

                { FromUnit = from
                  ToUnit = target
                  Weight = uses.Length
                  CrossProject = targetProject <> project.Id
                  SampleLocations = uses |> Array.truncate 3 |> Array.map (fun (_, _, _, s) -> toLocation s) |> List.ofArray })
            |> List.ofSeq

        { Units = units
          Dependencies = edges
          TypeDependencies = typeEdges
          Diagnostics = errors }

    /// What a project's structure depends on: its sources, defines, referenced assemblies, and which assemblies
    /// belong to which project of the solution.
    let private cacheKey (index: SolutionIndex) (project: LoadedProject) =
        Caching.hash
            [ yield "structure-csharp-v3"
              yield typeof<StructureResult>.Assembly.GetName().Version.ToString()
              yield typeof<CSharpCompilation>.Assembly.GetName().Version.ToString()
              yield project.Id
              yield project.Path
              yield property project "DefineConstants"
              for f in project.SourceFiles do
                  yield f
                  yield (if File.Exists f then Caching.fileHash f else "")
              for r in project.ReferencePaths do
                  let info = FileInfo r
                  yield (if info.Exists then $"{r}|{info.Length}|{info.LastWriteTimeUtc.Ticks}" else r)
              for p in index.Projects do
                  yield $"{p.AssemblyName}={p.Id}:{SolutionIndex.supportLevel p index}" ]

    let create (store: Caching.Store) =
        { new IStructureAnalyzer with
            member _.Language = Language.CSharp

            member _.Analyze(projects, index) =
                projects
                |> List.map (fun p ->
                    try
                        let key = cacheKey index p

                        match store.TryRead<StructureResult>("structure", key) with
                        | Some cached ->
                            Progress.report $"Structure of {p.Name}: cached"
                            cached
                        | None ->
                            Progress.report $"Structure of {p.Name}: compiling its C# code"
                            let result = analyzeProject index p

                            // Results with compile errors may change once the code is fixed: not cached.
                            if result.Diagnostics.IsEmpty then
                                store.Write("structure", key, result)

                            result
                    with e ->
                        { StructureResult.empty with
                            Diagnostics =
                                [ { Severity = Floorplan.Model.DiagnosticSeverity.Error
                                    Code = "structure.failed"
                                    Message = $"{p.Name}: structure analysis failed: {e.Message}"
                                    ProjectId = Some p.Id
                                    Location = None } ] })
                |> StructureResult.combine }
