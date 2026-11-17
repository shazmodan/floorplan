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

    /// A Roslyn compilation of the project from its MSBuild evaluation: its compile items and resolved references.
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

        CSharpCompilation.Create(
            project.AssemblyName,
            trees,
            references,
            CSharpCompilationOptions(kind, nullableContextOptions = NullableContextOptions.Enable, allowUnsafe = true)
        )

    /// Each namespace that declares a type in the project's own source, with where it is first declared.
    let private declaredNamespaces (trees: SyntaxTree list) =
        trees
        |> List.filter (fun t -> not (isGenerated t.FilePath))
        |> List.collect (fun tree ->
            let root = tree.GetRoot()

            let rec walk (prefix: string) (node: SyntaxNode) =
                [ for child in node.ChildNodes() do
                      match child with
                      | :? BaseNamespaceDeclarationSyntax as ns ->
                          let name = if prefix = "" then ns.Name.ToString() else prefix + "." + ns.Name.ToString()
                          let own = ns.Members |> Seq.exists (fun m -> not (m :? BaseNamespaceDeclarationSyntax))

                          if own then
                              yield name, Some(toLocation (ns.GetLocation().GetLineSpan()))

                          yield! walk name ns
                      | :? BaseTypeDeclarationSyntax
                      | :? DelegateDeclarationSyntax -> yield prefix, None
                      | :? GlobalStatementSyntax when prefix = "" -> yield "", None
                      | _ -> () ]

            walk "" root)
        |> List.groupBy fst
        |> List.map (fun (name, found) -> name, found |> List.tryPick snd)
        |> List.sortBy fst

    let private analyzeProject (index: SolutionIndex) (project: LoadedProject) =
        let compilation = compilation project
        let unitId = UnitId.create project.Id

        let namespaces = declaredNamespaces (List.ofSeq compilation.SyntaxTrees)

        let units =
            namespaces
            |> List.map (fun (name, location) ->
                { Id = unitId name
                  ProjectId = project.Id
                  Kind = UnitKind.Namespace
                  Name = name
                  DisplayName = if name = "" then globalNamespaceLabel else name.Split('.') |> Array.last
                  Parent = None
                  Location = location })

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
            |> Seq.filter (fun t -> not (isGenerated t.FilePath))
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
                            unitId (enclosingNamespace model n.SpanStart), t, targetProject, n.GetLocation().GetLineSpan())))
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
          Diagnostics = errors }

    /// What a project's structure depends on: its sources, defines, referenced assemblies, and which assemblies
    /// belong to which project of the solution.
    let private cacheKey (index: SolutionIndex) (project: LoadedProject) =
        Caching.hash
            [ yield "structure-csharp-v1"
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
                        | Some cached -> cached
                        | None ->
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
