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

let private languageName (language: Language) =
    match language with
    | Language.FSharp -> "F#"
    | Language.CSharp -> "C#"
    | Language.OtherLanguage -> "other language"

let private idOf (kind: C4Kind) (ext: External) =
    (if kind = C4Kind.Person then "person:" else "external:") + ext.Key

/// The id of a project's container.
let containerId (projectId: string) = "container:" + projectId

/// A relationship as found, before relationships with the same ends and description are merged.
type private Link =
    { From: string
      To: string
      Description: string
      Technology: string option
      Source: string }

let private merge (links: Link list) : C4Relationship list =
    links
    |> List.groupBy (fun l -> l.From, l.To, l.Description)
    |> List.map (fun ((from, target, description), ls) ->
        { From = from
          To = target
          Description = description
          Technology = joined (ls |> List.choose (fun l -> l.Technology))
          Sources = ls |> List.map (fun l -> l.Source) |> List.distinct |> List.sort })
    |> List.sortBy (fun r -> r.From, r.To, r.Description)

/// The path of a URL, without its scheme and host: what another application's route would be.
let private pathOf (url: string) =
    if url.StartsWith "http://" || url.StartsWith "https://" then
        let rest = url.Substring(url.IndexOf "//" + 2)

        match rest.IndexOf '/' with
        | -1 -> "/"
        | i -> rest.Substring i
    elif url.StartsWith "/" then
        url
    else
        "/" + url

/// What an application is, from how its code is started: the kinds of its entry points.
let private kindsOf (entries: EntryPoint list) =
    let has (categories: EffectCategory list) =
        entries |> List.exists (fun e -> List.contains e.Category categories)

    [ if has [ EffectCategory.HttpIn ] then "web application"
      if has [ EffectCategory.Network ] then "message consumer"
      if has [ EffectCategory.FilesystemRead; EffectCategory.FilesystemWrite ] then "file watcher"
      if has [ EffectCategory.Background ] then "background worker" ]

let private sentence (kinds: string list) =
    match kinds with
    | [] -> "An application"
    | [ one ] -> $"A {one}"
    | many -> "A " + String.Join(", ", List.take (many.Length - 1) many) + " and " + List.last many

/// Everything the levels are derived from, worked out once.
type private Facts =
    { Model: AnalysisModel
      Exits: ExitPoint list
      Solution: Solution
      Library: ExitPoint -> string option
      /// The containers each exit point belongs to: its application's, or for library code, the applications
      /// whose entry points reach it (every one that references the library, when none is traced to it).
      ContainersOf: ExitPoint -> string list
      /// The projects that are containers: applications, and libraries no application references.
      Containers: Project list
      /// The units of a container's code: its project's and those of every project it references.
      UnitsOf: string -> Set<string> }

let private factsOf (model: AnalysisModel) : Facts =
    let exits = model.ExitPoints |> List.filter (shown model)

    let solution =
        { Namespaces = model.Units |> List.map (fun u -> u.Name) |> Set.ofList
          Assemblies = model.Projects |> List.map (fun p -> p.Name) |> Set.ofList }

    let root = rootsOf (exits |> List.choose (libraryIn solution))
    let projects = model.Projects |> List.filter (fun p -> not p.IsTestProject)
    let byId = projects |> List.map (fun p -> p.Id, p) |> Map.ofList
    let projectOfUnit = model.Units |> List.map (fun u -> u.Id, u.ProjectId) |> Map.ofList

    // Every project a project references, directly or not, itself included.
    let rec closure (seen: Set<string>) (id: string) =
        if seen.Contains id then
            seen
        else
            match byId.TryFind id with
            | Some p -> p.ProjectReferences |> List.fold closure (seen.Add id)
            | None -> seen.Add id

    let applications = projects |> List.filter (fun p -> p.IsApplication)
    let closures = applications |> List.map (fun a -> a.Id, closure Set.empty a.Id) |> Map.ofList

    let usedBy (projectId: string) =
        applications |> List.filter (fun a -> closures[a.Id].Contains projectId) |> List.map (fun a -> a.Id)

    let applicationOfEntry = model.EntryPoints |> List.map (fun e -> e.Id, e.ApplicationId) |> Map.ofList

    let reachedBy =
        model.Traces
        |> List.collect (fun t ->
            match applicationOfEntry.TryFind t.EntryPointId with
            | Some app -> t.ExitPointIds |> List.map (fun x -> x, app)
            | None -> [])
        |> List.groupBy fst
        |> List.map (fun (x, apps) -> x, apps |> List.map snd |> Set.ofList)
        |> Map.ofList

    let containers =
        applications @ (projects |> List.filter (fun p -> not p.IsApplication && (usedBy p.Id).IsEmpty))

    let containersOf (x: ExitPoint) =
        match projectOfUnit.TryFind x.Unit with
        | None -> []
        | Some p when byId.TryFind p |> Option.exists (fun p -> p.IsApplication) -> [ containerId p ]
        | Some p ->
            let users = usedBy p
            let traced = users |> List.filter (fun a -> reachedBy.TryFind x.Id |> Option.exists (fun s -> s.Contains a))

            match traced, users with
            | _ :: _, _ -> traced |> List.map containerId
            | [], _ :: _ -> users |> List.map containerId
            | [], [] -> [ containerId p ]

    { Model = model
      Exits = exits
      Solution = solution
      Library = fun x -> libraryIn solution x |> Option.map root
      ContainersOf = containersOf
      Containers = containers
      UnitsOf =
        let byContainer =
            containers
            |> List.map (fun p ->
                let own = closure Set.empty p.Id
                containerId p.Id, model.Units |> List.filter (fun u -> own.Contains u.ProjectId) |> List.map (fun u -> u.Id) |> Set.ofList)
            |> Map.ofList

        fun c -> byContainer.TryFind c |> Option.defaultValue Set.empty }

/// The id of a unit's component in a container: a library's unit is a component of each container that has it.
let componentId (container: string) (unitId: string) = $"component:{container.Substring 10}|{unitId}"

/// Level 1, 2 and 3: the solution as one system, its containers and their components, who calls them and what
/// they use.
let private elementsAndLinks (facts: Facts) : C4Element list * Link list =
    let model = facts.Model
    let units = model.Units |> List.map (fun u -> u.Id, u) |> Map.ofList

    // The component of a unit in a container, or the container itself for a unit it does not have.
    let partOf (container: string) (unitId: string) =
        if (facts.UnitsOf container).Contains unitId then componentId container unitId else container
    let applications = model.Projects |> List.filter (fun p -> p.IsApplication)

    let system =
        { element SystemId C4Kind.SoftwareSystem model.Solution.Name
              $"The {model.Solution.Name} solution: {applications.Length} applications in {model.Projects.Length} projects" with
            Sources = model.Projects |> List.map (fun p -> p.Id) }

    let containerElements =
        facts.Containers
        |> List.map (fun p ->
            let entries = model.EntryPoints |> List.filter (fun e -> e.ApplicationId = p.Id)
            let kinds = if p.IsApplication then kindsOf entries else [ "library" ]
            let description = if p.IsApplication then sentence kinds else "A library no application references"

            { element (containerId p.Id) C4Kind.Container p.Name description with
                Technology = Some $"{languageName p.Language} · {p.TargetFramework}"
                Parent = Some SystemId
                Tags = kinds
                Sources = p.Id :: (entries |> List.map (fun e -> e.Id)) })

    // One application sends what another receives: a message to a queue it consumes (every consumer of the
    // queue), or a request to its route, when the URL names no host but a local one or the application's own name
    // and exactly one other application has the route. Only exact matches of known text.
    let localHost (url: string) (application: string) =
        match hostOf (Some { Role = ""; Kind = PayloadKind.Literal; Value = Some url }) with
        | None -> true
        | Some host ->
            let name = host.Split(':').[0].ToLowerInvariant()
            name = "localhost" || name = "127.0.0.1" || name = application.ToLowerInvariant()

    let between =
        [ for x in facts.Exits do
              match x.Category, x.Payload with
              | EffectCategory.Network, Some { Kind = PayloadKind.Literal; Value = Some queue } ->
                  for e in model.EntryPoints do
                      if e.Category = EffectCategory.Network && e.Route = Some queue then
                          for c in facts.ContainersOf x do
                              if c <> containerId e.ApplicationId then
                                  yield
                                      { From = partOf c x.Unit
                                        To = partOf (containerId e.ApplicationId) e.HandlerUnit
                                        Description = $"sends messages to {queue}"
                                        Technology = technologyOf facts.Solution facts.Library x
                                        Source = x.Id }
              | EffectCategory.HttpOut, Some { Kind = PayloadKind.Literal; Value = Some url } ->
                  let path = pathOf url

                  for c in facts.ContainersOf x do
                      let receivers =
                          model.EntryPoints
                          |> List.filter (fun e -> e.Category = EffectCategory.HttpIn && e.Route = Some path && containerId e.ApplicationId <> c)

                      match receivers |> List.map (fun e -> e.ApplicationId) |> List.distinct with
                      | [ application ] when localHost url application ->
                          yield
                              { From = partOf c x.Unit
                                To = partOf (containerId application) receivers.Head.HandlerUnit
                                Description = $"sends HTTP requests to {path}"
                                Technology = technologyOf facts.Solution facts.Library x
                                Source = x.Id }
                      | _ -> ()
              | _ -> () ]

    // A call matched to another application goes there, not to an external system.
    let matched = between |> List.map (fun l -> l.Source) |> Set.ofList

    let outbound =
        facts.Exits |> List.filter (fun x -> not (matched.Contains x.Id)) |> List.map (fun x -> externalOf facts.Library x, x)

    let inbound = model.EntryPoints |> List.choose (fun e -> callerOf e |> Option.map (fun c -> c, e))

    let externals =
        (outbound |> List.map (fun (ext, x) -> (C4Kind.ExternalSystem, ext), x.Id))
        @ (inbound |> List.map (fun (c, e) -> c, e.Id))
        |> List.groupBy (fun ((kind, ext), _) -> idOf kind ext)
        |> List.map (fun (id, items) ->
            let (kind, ext), _ = items.Head
            { element id kind ext.Name ext.Description with Sources = items |> List.map snd |> List.distinct |> List.sort })

    let uses =
        outbound
        |> List.collect (fun (ext, x) ->
            facts.ContainersOf x
            |> List.map (fun c ->
                { From = partOf c x.Unit
                  To = idOf C4Kind.ExternalSystem ext
                  Description = verb x.Category
                  Technology = technologyOf facts.Solution facts.Library x
                  Source = x.Id }))

    let calls =
        inbound
        |> List.map (fun ((kind, ext), e) ->
            { From = idOf kind ext
              To = partOf (containerId e.ApplicationId) e.HandlerUnit
              Description = callerVerb e
              Technology = (if e.Category = EffectCategory.HttpIn then Some "HTTP" else None)
              Source = e.Id })

    // Dependencies between the units of a container's code.
    let uses' =
        [ for c in facts.Containers do
              let id = containerId c.Id
              let own = facts.UnitsOf id

              for d in model.Dependencies do
                  if d.FromUnit <> d.ToUnit && own.Contains d.FromUnit && own.Contains d.ToUnit then
                      yield
                          { From = componentId id d.FromUnit
                            To = componentId id d.ToUnit
                            Description = "uses"
                            Technology = None
                            Source = $"{d.FromUnit} -> {d.ToUnit}" } ]

    let links = calls @ uses @ between @ uses'

    let components =
        [ for c in facts.Containers do
              let id = containerId c.Id

              for unitId in facts.UnitsOf id |> Set.toList do
                  let u = units[unitId]
                  let componentKey = componentId id unitId
                  let project = model.Projects |> List.tryFind (fun p -> p.Id = u.ProjectId)

                  let kind =
                      match u.Kind with
                      | UnitKind.Module -> "module"
                      | UnitKind.Namespace -> "namespace"
                      | UnitKind.Type -> "type"
                      | UnitKind.ProjectUnit -> "project"

                  let language = project |> Option.map (fun p -> languageName p.Language) |> Option.defaultValue ""
                  let handles = links |> List.exists (fun l -> l.To = componentKey && not (l.From.StartsWith "component:"))
                  let reaches = links |> List.exists (fun l -> l.From = componentKey && not (l.To.StartsWith "component:"))
                  let projectName = project |> Option.map (fun p -> p.Name) |> Option.defaultValue u.ProjectId

                  yield
                      { element componentKey C4Kind.Component u.Name $"A {language} {kind} of {projectName}" with
                          Technology = Some $"{language} {kind}"
                          Parent = Some id
                          Tags =
                            [ if u.ProjectId <> c.Id then $"library {projectName}"
                              if handles then "handles requests"
                              if reaches then "reaches out" ]
                          Sources = [ unitId ] } ]

    system :: containerElements @ components @ (externals |> List.sortBy (fun e -> e.Id)), links

/// The model's links as one view shows them: each end moved up to the shown element that contains it, links
/// within one shown element dropped, and the rest merged.
let private lifted (elements: Map<string, C4Element>) (shown: string list) (links: Link list) =
    let shownSet = Set.ofList shown

    let rec within (id: string) (guard: int) =
        if shownSet.Contains id then
            Some id
        elif guard > 16 then
            None
        else
            match elements.TryFind id |> Option.bind (fun e -> e.Parent) with
            | Some parent -> within parent (guard + 1)
            | None -> None

    links
    |> List.choose (fun l ->
        match within l.From 0, within l.To 0 with
        | Some f, Some t when f <> t -> Some { l with From = f; To = t }
        | _ -> None)
    |> merge

/// The C4 model of the solution the analysis model describes.
let derive (model: AnalysisModel) : C4Model =
    let facts = factsOf model
    let elements, links = elementsAndLinks facts
    let byId = elements |> List.map (fun e -> e.Id, e) |> Map.ofList

    let ofKind (kinds: C4Kind list) =
        elements |> List.filter (fun e -> List.contains e.Kind kinds) |> List.map (fun e -> e.Id)

    let outside = ofKind [ C4Kind.Person; C4Kind.ExternalSystem ]

    let view (id: string) (level: C4Level) (scope: string) (shown: string list) =
        { Id = id
          Level = level
          Scope = scope
          Elements = shown
          Relationships = lifted byId shown links
          Layout = AnalysisModel.emptyGraph }

    let containers = ofKind [ C4Kind.Container ]

    // A container's components, and the elements outside it they are related to.
    let componentsView (container: string) =
        let own = elements |> List.filter (fun e -> e.Parent = Some container) |> List.map (fun e -> e.Id)
        let around = outside @ (containers |> List.filter (fun c -> c <> container))
        let all = lifted byId (own @ around) links
        let ownSet = Set.ofList own

        let related =
            around
            |> List.filter (fun a -> all |> List.exists (fun r -> (r.From = a && ownSet.Contains r.To) || (r.To = a && ownSet.Contains r.From)))

        view $"components:{container.Substring 10}" C4Level.Components container (own @ related)

    { Elements = elements
      Relationships = merge links
      Views =
        [ view "context" C4Level.Context SystemId (SystemId :: outside)
          view "containers" C4Level.Containers SystemId (containers @ outside) ]
        @ (containers |> List.map componentsView) }

/// The model as text, one line per element and relationship, then each view with its relationships, for people
/// and golden files.
let describe (c4: C4Model) : string list =
    let kind (k: C4Kind) = Json.unionName k

    let relationship (r: C4Relationship) =
        let tech = r.Technology |> Option.defaultValue "-"
        $"{r.From} -> {r.To} | {r.Description} | {tech} | {r.Sources.Length} sources"

    [ for e in c4.Elements do
          let tech = e.Technology |> Option.defaultValue "-"
          let parent = e.Parent |> Option.defaultValue "-"
          let tags = if e.Tags.IsEmpty then "-" else String.Join(",", e.Tags)
          yield $"{kind e.Kind} | {e.Id} | {e.Name} | {tech} | in {parent} | {tags} | {e.Sources.Length} sources"
      for r in c4.Relationships do
          yield relationship r
      for v in c4.Views do
          yield $"view {v.Id} | {Json.unionName v.Level} of {v.Scope} | {v.Elements.Length} elements"

          for r in v.Relationships do
              yield "  " + relationship r ]
