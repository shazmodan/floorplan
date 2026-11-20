/// Objects a library makes by reflection from an XML configuration file (catalog rules of kind `configured`):
/// NLog's targets in nlog.config, log4net's appenders in log4net.config. The configuration files are those in a
/// project's output folder, where the library finds them at run time. Each type they name is constructed, as if in
/// code, so the virtual calls the library makes on it reach it.
module Floorplan.Analysis.Configured

open System
open System.IO
open System.Xml
open System.Xml.Linq
open Floorplan.Model
open Floorplan.IL
open Floorplan.Catalog

/// A type a configuration file names.
type ConfiguredType =
    { Type: TypeRef
      Rule: Rule
      /// The configuration file, and the value that named the type.
      File: string
      Named: string }

/// The XML files in a folder that can hold a library's configuration: `*.config` (nlog.config, app.config as
/// `App.dll.config`, web.config), and `*.xml` files named after a configuration's root (log4net.xml).
let private candidates (folder: string) (roots: string list) =
    if not (Directory.Exists folder) then
        []
    else
        let named (f: string) =
            let n = Path.GetFileName(f).ToLowerInvariant()
            roots |> List.exists (fun r -> n.Contains(r.ToLowerInvariant()))

        Directory.EnumerateFiles folder
        |> Seq.filter (fun f ->
            let ext = Path.GetExtension(f).ToLowerInvariant()
            ext = ".config" || (ext = ".xml" && named f))
        |> Seq.sort
        |> Seq.toList

let private load (file: string) =
    try
        let settings = XmlReaderSettings(DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null)
        use reader = XmlReader.Create(file, settings)
        Ok(XDocument.Load reader)
    with
    | :? XmlException
    | :? IOException
    | :? UnauthorizedAccessException as e -> Error e.Message

/// The element a configuration lives in: the root itself, or a child of a <configuration> root.
let private sections (doc: XDocument) (root: string) =
    match doc.Root with
    | null -> []
    | r when r.Name.LocalName = root -> [ r ]
    | r when r.Name.LocalName = "configuration" -> r.Elements() |> Seq.filter (fun e -> e.Name.LocalName = root) |> Seq.toList
    | _ -> []

/// The type values a configuration section names, in document order.
let private named (section: XElement) (spec: ConfigurationSpec) =
    section.Descendants()
    |> Seq.filter (fun e -> spec.Elements |> List.contains e.Name.LocalName)
    |> Seq.choose (fun e ->
        e.Attributes()
        |> Seq.tryFind (fun a -> a.Name.LocalName = spec.TypeAttribute)
        |> Option.map (fun a -> a.Value.Trim()))
    |> Seq.filter (fun v -> v <> "")
    |> Seq.toList

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

    /// The types named in the configuration files of these folders, and a warning for each value that names no
    /// type the analysis can read.
    member this.Find(rules: Rule list, folders: string list) : ConfiguredType list * Diagnostic list =
        let rules = rules |> List.filter (fun r -> r.Kind = Configured && r.Configuration.IsSome) |> List.distinctBy (fun r -> r.Id)

        if rules.IsEmpty then
            [], []
        else
            let roots = rules |> List.map (fun r -> r.Configuration.Value.Root) |> List.distinct

            let found =
                [ for folder in List.distinct folders do
                      for file in candidates folder roots do
                          match load file with
                          | Error _ -> ()
                          | Ok doc ->
                              for rule in rules do
                                  let spec = rule.Configuration.Value

                                  for section in sections doc spec.Root do
                                      for value in named section spec ->
                                          file, rule, value, this.Resolve(rule, value) ]

            let types =
                found
                |> List.choose (fun (file, rule, value, t) ->
                    t |> Option.map (fun t -> { Type = t; Rule = rule; File = file; Named = value }))
                |> List.distinctBy (fun c -> c.Type)

            let unresolved =
                found
                |> List.filter (fun (_, _, _, t) -> t.IsNone)
                |> List.distinctBy (fun (file, _, value, _) -> file, value)
                |> List.map (fun (file, rule, value, _) ->
                    { Severity = DiagnosticSeverity.Warning
                      Code = "configured.unresolved"
                      Message =
                        $"{Path.GetFileName file} names \"{value}\" (rule {rule.Id}), which is not a type the analysis can read. What it does is not analyzed."
                      ProjectId = None
                      Location = None })

            types, unresolved
