/// Objects a library makes by reflection from a configuration file (catalog rules of kind `configured`): NLog's
/// targets in nlog.config or appsettings.json, log4net's appenders in log4net.config. The configuration files are
/// those in a project's output folder, where the library finds them at run time. Each type they name is
/// constructed, as if in code, so the virtual calls the library makes on it reach it.
module Floorplan.Analysis.Configured

open System
open System.IO
open System.Text.Json
open System.Xml
open System.Xml.Linq
open Floorplan.Model
open Floorplan.IL
open Floorplan.Catalog

/// What the library makes of what a configuration file names.
type Made =
    /// A type it constructs.
    | Constructed of TypeRef
    /// A method it calls.
    | Called of MethodRef

/// A type or method a configuration file names.
type ConfiguredType =
    { Made: Made
      Rule: Rule
      /// The configuration file, and the value that named the type.
      File: string
      Named: string
      /// The members that load the file (the catalog's `loadedBy`): a call to one makes the object.
      LoadedBy: string list }

/// The XML files in a folder that can hold a library's configuration: `*.config` (nlog.config, app.config as
/// `App.dll.config`, web.config), and `*.xml` files named after a configuration's root (log4net.xml).
let private xmlFiles (folder: string) (roots: string list) =
    let named (f: string) =
        let n = Path.GetFileName(f).ToLowerInvariant()
        roots |> List.exists (fun r -> n.Contains(r.ToLowerInvariant()))

    Directory.EnumerateFiles folder
    |> Seq.filter (fun f ->
        let ext = Path.GetExtension(f).ToLowerInvariant()
        ext = ".config" || (ext = ".xml" && named f))
    |> Seq.sort
    |> Seq.toList

/// The JSON files in a folder that hold an application's settings: appsettings.json and its per-environment
/// variants (appsettings.Production.json).
let private jsonFiles (folder: string) =
    Directory.EnumerateFiles(folder, "appsettings*.json") |> Seq.sort |> Seq.toList

let private loadXml (file: string) =
    try
        let settings = XmlReaderSettings(DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null)
        use reader = XmlReader.Create(file, settings)
        Some(XDocument.Load reader)
    with
    | :? XmlException
    | :? IOException
    | :? UnauthorizedAccessException -> None

/// The type values an XML configuration names, in document order.
let private xmlValues (doc: XDocument) (spec: XmlConfiguration) =
    // The element a configuration lives in: the root itself, or a child of a <configuration> root.
    let sections =
        match doc.Root with
        | null -> []
        | r when r.Name.LocalName = spec.Root -> [ r ]
        | r when r.Name.LocalName = "configuration" ->
            r.Elements() |> Seq.filter (fun e -> e.Name.LocalName = spec.Root) |> Seq.toList
        | _ -> []

    [ for section in sections do
          for e in section.Descendants() do
              if spec.Elements |> List.contains e.Name.LocalName then
                  match e.Attributes() |> Seq.tryFind (fun a -> a.Name.LocalName = spec.TypeAttribute) with
                  | Some a when a.Value.Trim() <> "" -> yield a.Value.Trim()
                  | _ -> () ]

let private loadJson (file: string) =
    try
        let text = File.ReadAllText file
        Some(JsonDocument.Parse(text, JsonDocumentOptions(CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)))
    with
    | :? JsonException
    | :? IOException
    | :? UnauthorizedAccessException -> None

/// The settings the application's configuration files in a folder give, by key ("Section:Key", ignoring case, an
/// array's elements by index), as Microsoft.Extensions.Configuration flattens them: appsettings.json, then
/// appsettings.Production.json over it, as in the default environment, then the files the application adds
/// (`added`, paths relative to the folder, in the order added) over those.
let appSettings (folder: string) (added: string list) : Collections.Generic.IReadOnlyDictionary<string, string> =
    let values = Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

    let rec flatten (prefix: string) (e: JsonElement) =
        let under (k: string) = if prefix = "" then k else prefix + ":" + k

        match e.ValueKind with
        | JsonValueKind.Object ->
            for p in e.EnumerateObject() do
                flatten (under p.Name) p.Value
        | JsonValueKind.Array -> e.EnumerateArray() |> Seq.iteri (fun i x -> flatten (under (string i)) x)
        | JsonValueKind.String -> values[prefix] <- e.GetString()
        | JsonValueKind.Null -> values.Remove prefix |> ignore
        | _ -> values[prefix] <- e.GetRawText()

    for name in [ "appsettings.json"; "appsettings.Production.json" ] @ added do
        let file = Path.Combine(folder, name)

        if File.Exists file then
            match loadJson file with
            | Some doc ->
                use doc = doc
                flatten "" doc.RootElement
            | None -> ()

    values

/// The type values a JSON configuration names, in document order. Configuration keys ignore case, as
/// Microsoft.Extensions.Configuration's do.
let private jsonValues (doc: JsonDocument) (spec: JsonConfiguration) =
    let same (a: string) (b: string) = String.Equals(a, b, StringComparison.OrdinalIgnoreCase)

    let typeOf (o: JsonElement) =
        o.EnumerateObject()
        |> Seq.tryFind (fun p -> same p.Name spec.TypeProperty && p.Value.ValueKind = JsonValueKind.String)
        |> Option.map (fun p -> p.Value.GetString().Trim())
        |> Option.filter (fun v -> v <> "")

    // An element of a list or map of named things: an object with the property, or a plain name.
    let element (e: JsonElement) =
        match e.ValueKind with
        | JsonValueKind.Object -> typeOf e
        | JsonValueKind.String -> Some(e.GetString().Trim()) |> Option.filter (fun v -> v <> "")
        | _ -> None

    // Objects named by one of the properties, as the property's value or as elements of its object or array.
    let rec walk (e: JsonElement) : string list =
        match e.ValueKind with
        | JsonValueKind.Object ->
            [ for p in e.EnumerateObject() do
                  if spec.Properties |> List.exists (same p.Name) then
                      match p.Value.ValueKind with
                      | JsonValueKind.Object ->
                          match typeOf p.Value with
                          | Some t -> yield t
                          | None ->
                              for inner in p.Value.EnumerateObject() do
                                  yield! element inner.Value |> Option.toList
                      | JsonValueKind.Array ->
                          for inner in p.Value.EnumerateArray() do
                              yield! element inner |> Option.toList
                      | _ -> ()

                  yield! walk p.Value ]
        | JsonValueKind.Array -> [ for x in e.EnumerateArray() do yield! walk x ]
        | _ -> []

    match doc.RootElement.ValueKind with
    | JsonValueKind.Object ->
        doc.RootElement.EnumerateObject()
        |> Seq.filter (fun p -> same p.Name spec.Section)
        |> Seq.collect (fun p -> walk p.Value)
        |> Seq.toList
    | _ -> []

/// Finds the types configuration files name, for the configured rules of the catalogs in use.
type Resolver(set: AssemblySet) =
    // The assemblies a rule's types can be in: the rule's own and the dependencies that reference it.
    let assembliesFor (rule: Rule) =
        set.KnownAssemblies
        |> List.filter (fun n -> set.Origin n <> Framework)
        |> List.choose set.TryLoad
        |> List.filter (fun a ->
            String.Equals(a.Name, rule.Assembly, StringComparison.OrdinalIgnoreCase)
            || a.ReferencedAssemblies |> List.exists (fun r -> String.Equals(r, rule.Assembly, StringComparison.OrdinalIgnoreCase)))

    let concrete (t: TypeDef) (rule: Rule) =
        not t.IsAbstract && not t.IsInterface && not t.IsGeneric && set.IsSubtypeOf(t.Ref, rule.Type)

    let aliases = Collections.Generic.Dictionary<string, (string * TypeRef) list>()

    let aliasesFor (rule: Rule) (attribute: string) =
        match aliases.TryGetValue rule.Id with
        | true, a -> a
        | _ ->
            let a =
                assembliesFor rule
                |> List.collect (fun asm ->
                    asm.Types
                    |> List.filter (fun t -> concrete t rule)
                    |> List.collect (fun t ->
                        t.Attributes
                        |> List.filter (fun at -> at.Type.FullName = attribute)
                        |> List.choose (fun at ->
                            match at.Arguments with
                            | AttributeValue.Text name :: _ -> Some(name, t.Ref)
                            | _ -> None)))

            aliases[rule.Id] <- a
            a

    /// The type a value names: a full name, optionally followed by ", Assembly", or an alias.
    member _.Resolve(rule: Rule, value: string) : TypeRef option =
        let typeName, assembly =
            match value.IndexOf ',' with
            | -1 -> value, None
            | i -> value.Substring(0, i).Trim(), Some(value.Substring(i + 1).Split(',').[0].Trim())

        let byName =
            assembliesFor rule
            |> List.filter (fun a ->
                match assembly with
                | Some n -> String.Equals(a.Name, n, StringComparison.OrdinalIgnoreCase)
                | None -> true)
            |> List.tryPick (fun a -> a.Types |> List.tryFind (fun t -> t.Ref.FullName = typeName && concrete t rule))
            |> Option.map (fun t -> t.Ref)

        byName
        |> Option.orElseWith (fun () ->
            match rule.Configuration |> Option.bind (fun c -> c.Alias) with
            | Some attribute ->
                aliasesFor rule attribute
                |> List.tryFind (fun (alias, _) -> String.Equals(alias, value, StringComparison.OrdinalIgnoreCase))
                |> Option.map snd
            | None -> None)

    /// The methods a name calls: methods of the rule's type, and extension methods on it, of that name.
    member _.ResolveMethods(rule: Rule, value: string) : MethodRef list =
        let named (m: MethodDef) = String.Equals(m.Ref.Name, value, StringComparison.OrdinalIgnoreCase)

        assembliesFor rule
        |> List.collect (fun a ->
            a.Types
            |> List.collect (fun t ->
                if t.Ref.FullName = rule.Type then
                    t.Methods |> List.filter (fun m -> named m && not m.IsStatic && m.IsPublic)
                else
                    t.Methods
                    |> List.filter (fun m ->
                        named m
                        && m.IsStatic
                        && m.IsPublic
                        && (match m.Ref.ParameterTypes with
                            | first :: _ -> first = rule.Type
                            | [] -> false))))
        |> List.map (fun m -> m.Ref)

    /// The types named in the configuration files of these folders, and a warning for each value that names no
    /// type the analysis can read.
    member this.Find(rules: Rule list, folders: string list) : ConfiguredType list * Diagnostic list =
        let rules = rules |> List.filter (fun r -> r.Kind = Configured && r.Configuration.IsSome) |> List.distinctBy (fun r -> r.Id)
        let xmlRules = rules |> List.choose (fun r -> r.Configuration.Value.Xml |> Option.map (fun x -> r, x))
        let jsonRules = rules |> List.choose (fun r -> r.Configuration.Value.Json |> Option.map (fun j -> r, j))
        let folders = folders |> List.distinct |> List.filter Directory.Exists

        let fromXml =
            if xmlRules.IsEmpty then
                []
            else
                let roots = xmlRules |> List.map (fun (_, x) -> x.Root) |> List.distinct

                [ for folder in folders do
                      for file in xmlFiles folder roots do
                          match loadXml file with
                          | Some doc ->
                              for (rule, spec) in xmlRules do
                                  for value in xmlValues doc spec -> file, rule, value, spec.LoadedBy
                          | None -> () ]

        let fromJson =
            if jsonRules.IsEmpty then
                []
            else
                [ for folder in folders do
                      for file in jsonFiles folder do
                          match loadJson file with
                          | Some doc ->
                              use doc = doc

                              for (rule, spec) in jsonRules do
                                  for value in jsonValues doc spec -> file, rule, value, spec.LoadedBy
                          | None -> () ]

        let made (rule: Rule) (value: string) =
            match rule.Configuration.Value.Names with
            | ConfiguredName.Type -> this.Resolve(rule, value) |> Option.toList |> List.map Constructed
            | ConfiguredName.Method -> this.ResolveMethods(rule, value) |> List.map Called

        let found =
            (fromXml @ fromJson)
            |> List.map (fun (file, rule, value, loadedBy) -> file, rule, value, loadedBy, made rule value)

        // Once per file: each file is loaded by its own calls, for the project whose output folder holds it.
        let types =
            found
            |> List.collect (fun (file, rule, value, loadedBy, ms) ->
                ms |> List.map (fun m -> { Made = m; Rule = rule; File = file; Named = value; LoadedBy = loadedBy }))
            |> List.distinctBy (fun c -> c.Made, c.File)

        let unresolved =
            found
            |> List.filter (fun (_, _, _, _, ms) -> ms.IsEmpty)
            |> List.distinctBy (fun (file, _, value, _, _) -> file, value)
            |> List.map (fun (file, rule, value, _, _) ->
                { Severity = DiagnosticSeverity.Warning
                  Code = "configured.unresolved"
                  Message =
                    $"{Path.GetFileName file} names \"{value}\" (rule {rule.Id}), which is nothing the analysis can read. What it does is not analyzed."
                  ProjectId = None
                  Location = None })

        types, unresolved
