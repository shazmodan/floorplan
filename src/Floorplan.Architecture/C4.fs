/// The C4 model of an analyzed solution (docs/c4-plan.md): its four levels, derived from the analysis model's
/// projects, units, dependencies, entry points and exit points. Nothing here knows a language or a library: an
/// external system is named after what the analysis found (a URL's host, the namespace of the primitive that
/// reaches it), never after a list of known products.
module Floorplan.Architecture.C4

open System
open Floorplan.Model

/// The id of the element that stands for the solution.
[<Literal>]
let SystemId = "system"

/// The type part of a member, "Namespace.Type::Method" -> "Namespace.Type".
let private typeOf (memberName: string) =
    match memberName.IndexOf "::" with
    | -1 -> memberName
    | i -> memberName.Substring(0, i)

/// The namespace of a member's type, nested types included in their outer type.
let private namespaceOf (memberName: string) =
    let outer = (typeOf memberName).Split('/').[0]

    match outer.LastIndexOf '.' with
    | -1 -> ""
    | i -> outer.Substring(0, i)

/// A namespace of the platform itself, whose primitives name no system of their own.
let private isPlatform (ns: string) = ns = "System" || ns.StartsWith "System."

/// The host of an absolute URL at the start of a payload.
let private hostOf (payload: Payload option) =
    match payload |> Option.bind (fun p -> p.Value) with
    | Some value when value.StartsWith "http://" || value.StartsWith "https://" ->
        let rest = value.Substring(value.IndexOf "//" + 2)
        let host = rest.Split([| '/'; '?'; '#'; '{' |]).[0]
        if host = "" || host.Contains "…" then None else Some host
    | _ -> None

/// An external system: its id, name, technology, and what it is.
type private External =
    { Key: string
      Name: string
      Description: string }

let private generic (key: string) (name: string) (description: string) =
    { Key = key
      Name = name
      Description = description }

/// What the solution itself declares and builds, which names no external system: its namespaces and assemblies.
type private Solution =
    { Namespaces: Set<string>
      Assemblies: Set<string> }

/// The library a primitive belongs to, when it is neither the platform's nor the solution's own (a Refit
/// interface is declared in the solution).
let private libraryIn (solution: Solution) (x: ExitPoint) =
    let ns = namespaceOf x.Primitive
    if ns = "" || isPlatform ns || solution.Namespaces.Contains ns then None else Some ns

/// One library's namespaces named once: a namespace under another library's (Azure.Storage.Blobs.Specialized
/// under Azure.Storage.Blobs) is that library.
let private rootsOf (libraries: string list) =
    let all = libraries |> List.distinct

    fun (ns: string) ->
        all
        |> List.filter (fun other -> other <> ns && ns.StartsWith(other + "."))
        |> List.sortBy (fun other -> other.Length)
        |> List.tryHead
        |> Option.defaultValue ns

/// What an exit point reaches: the system the call talks to, not the library it goes through.
let private externalOf (library: ExitPoint -> string option) (x: ExitPoint) : External =
    match x.Category with
    | EffectCategory.HttpOut ->
        match hostOf x.Payload, library x with
        | Some host, _ -> generic $"http:{host}" host "An HTTP service"
        | None, Some lib -> generic $"library:{lib}" lib "A service reached through its client library"
        | None, None -> generic "http" "HTTP services" "HTTP services whose address is not known"
    | EffectCategory.Database ->
        match library x with
        | Some lib -> generic $"library:{lib}" lib "A database reached through its client library"
        | None -> generic "database" "Database" "A database reached through the platform's data access"
    | EffectCategory.Network ->
        match library x with
        | Some lib -> generic $"library:{lib}" lib "A service reached through its client library"
        | None -> generic "network" "Network" "Hosts reached over the network"
    | EffectCategory.FilesystemRead
    | EffectCategory.FilesystemWrite -> generic "filesystem" "File system" "Files and directories"
    | EffectCategory.HttpIn
    | EffectCategory.Background -> generic "other" "Other systems" "Systems the application reaches"
    | EffectCategory.Process -> generic "processes" "Operating system" "Processes the application starts or inspects"
    | EffectCategory.Native -> generic "native" "Native libraries" "Code outside .NET, called through P/Invoke"
    | EffectCategory.Environment -> generic "environment" "Environment" "Environment variables and the machine"
    | EffectCategory.Nondeterminism -> generic "clock" "Clock and randomness" "Time and random numbers"

/// What a call of a category does, as a relationship's description.
let private verb (category: EffectCategory) =
    match category with
    | EffectCategory.HttpOut -> "sends HTTP requests"
    | EffectCategory.Database -> "reads and writes data"
    | EffectCategory.Network -> "sends and receives over the network"
    | EffectCategory.FilesystemRead -> "reads files"
    | EffectCategory.FilesystemWrite -> "writes files"
    | EffectCategory.Process -> "starts or inspects processes"
    | EffectCategory.Native -> "calls native code"
    | EffectCategory.Environment -> "reads the environment"
    | EffectCategory.Nondeterminism -> "reads the clock or random numbers"
    | EffectCategory.HttpIn -> "receives HTTP requests"
    | EffectCategory.Background -> "runs background work"

/// How a call of a category is made, from the library it goes through.
let private technologyOf (solution: Solution) (library: ExitPoint -> string option) (x: ExitPoint) =
    match x.ViaAssembly |> Option.filter (fun a -> not (solution.Assemblies.Contains a)), library x with
    | Some via, _ -> Some via
    | None, Some lib -> Some lib
    | None, None ->
        match x.Category with
        | EffectCategory.HttpOut -> Some "HTTP"
        | _ -> None

/// Technologies joined for a label: the first few, and how many more there are.
let private joined (technologies: string list) =
    match technologies |> List.distinct |> List.sort with
    | [] -> None
    | few when few.Length <= 3 -> Some(String.Join(", ", few))
    | many -> Some(String.Join(", ", List.truncate 3 many) + $" and {many.Length - 3} more")

/// Who or what makes an entry point run: a person for HTTP, the system that delivers a message, the file system
/// for a watched folder. Background work starts by itself and has no caller.
let private callerOf (e: EntryPoint) : (C4Kind * External) option =
    match e.Category with
    | EffectCategory.HttpIn -> Some(C4Kind.Person, generic "user" "User" "A person or client calling the HTTP endpoints")
    | EffectCategory.Network ->
        Some(C4Kind.ExternalSystem, generic "messaging" "Message broker" "The broker or service that delivers messages")
    | EffectCategory.FilesystemRead
    | EffectCategory.FilesystemWrite -> Some(C4Kind.ExternalSystem, generic "filesystem" "File system" "Files and directories")
    | _ -> None

let private callerVerb (e: EntryPoint) =
    match e.Category with
    | EffectCategory.HttpIn -> "sends HTTP requests to"
    | EffectCategory.Network -> "delivers messages to"
    | _ -> "notifies of changed files"

/// The exit points shown by default: the website hides some categories and low-confidence ones.
let private shown (model: AnalysisModel) (x: ExitPoint) =
    not (model.Presentation.HiddenCategories |> List.contains x.Category)
    && not (model.Presentation.HiddenConfidence |> List.contains x.Confidence)

let private element (id: string) (kind: C4Kind) (name: string) (description: string) : C4Element =
    { Id = id
      Kind = kind
      Name = name
      Technology = None
      Description = description
      Parent = None
      Tags = []
      Sources = [] }

/// Level 1: the solution as one system, who calls it and what it uses.
let private context (model: AnalysisModel) : C4Element list * C4Relationship list =
    let exits = model.ExitPoints |> List.filter (shown model)
    let applications = model.Projects |> List.filter (fun p -> p.IsApplication)

    let system =
        { element SystemId C4Kind.SoftwareSystem model.Solution.Name
              $"The {model.Solution.Name} solution: {applications.Length} applications in {model.Projects.Length} projects" with
            Sources = model.Projects |> List.map (fun p -> p.Id) }

    let solution =
        { Namespaces = model.Units |> List.map (fun u -> u.Name) |> Set.ofList
          Assemblies = model.Projects |> List.map (fun p -> p.Name) |> Set.ofList }

    let root = rootsOf (exits |> List.choose (libraryIn solution))
    let library (x: ExitPoint) = libraryIn solution x |> Option.map root
    let outbound = exits |> List.map (fun x -> externalOf library x, x)
    let inbound = model.EntryPoints |> List.choose (fun e -> callerOf e |> Option.map (fun c -> c, e))

    let externals =
        (outbound |> List.map (fun (ext, x) -> (C4Kind.ExternalSystem, ext), x.Id))
        @ (inbound |> List.map (fun (c, e) -> c, e.Id))
        |> List.groupBy (fun ((_, ext), _) -> ext.Key)
        |> List.map (fun (key, items) ->
            let (kind, ext), _ = items.Head
            let id = (if kind = C4Kind.Person then "person:" else "external:") + key
            { element id kind ext.Name ext.Description with Sources = items |> List.map snd |> List.distinct |> List.sort })

    let idOf (kind: C4Kind) (ext: External) = (if kind = C4Kind.Person then "person:" else "external:") + ext.Key

    let uses =
        outbound
        |> List.groupBy (fun (ext, x) -> ext.Key, verb x.Category)
        |> List.map (fun ((_, description), items) ->
            let ext, _ = items.Head
            let xs = items |> List.map snd

            { From = SystemId
              To = idOf C4Kind.ExternalSystem ext
              Description = description
              Technology = joined (xs |> List.choose (technologyOf solution library))
              Sources = xs |> List.map (fun x -> x.Id) |> List.sort })

    let calls =
        inbound
        |> List.groupBy (fun ((kind, ext), e) -> idOf kind ext, callerVerb e)
        |> List.map (fun ((from, description), items) ->
            { From = from
              To = SystemId
              Description = description
              Technology =
                match (snd items.Head).Category with
                | EffectCategory.HttpIn -> Some "HTTP"
                | _ -> None
              Sources = items |> List.map (fun (_, e) -> e.Id) |> List.sort })

    system :: (externals |> List.sortBy (fun e -> e.Id)), (calls @ uses) |> List.sortBy (fun r -> r.From, r.To, r.Description)

/// The C4 model of the solution the analysis model describes.
let derive (model: AnalysisModel) : C4Model =
    let elements, relationships = context model

    { Elements = elements
      Relationships = relationships
      Views =
        [ { Id = "context"
            Level = C4Level.Context
            Scope = SystemId
            Elements = elements |> List.map (fun e -> e.Id)
            Layout = AnalysisModel.emptyGraph } ] }

/// The model as text, one line per element and relationship, for people and golden files.
let describe (c4: C4Model) : string list =
    let kind (k: C4Kind) = Json.unionName k

    [ for e in c4.Elements do
          let tech = e.Technology |> Option.defaultValue "-"
          let parent = e.Parent |> Option.defaultValue "-"
          let tags = if e.Tags.IsEmpty then "-" else String.Join(",", e.Tags)
          yield $"{kind e.Kind} | {e.Id} | {e.Name} | {tech} | in {parent} | {tags} | {e.Sources.Length} sources"
      for r in c4.Relationships do
          let tech = r.Technology |> Option.defaultValue "-"
          yield $"{r.From} -> {r.To} | {r.Description} | {tech} | {r.Sources.Length} sources"
      for v in c4.Views do
          yield $"view {v.Id} | {Json.unionName v.Level} of {v.Scope} | {v.Elements.Length} elements" ]
