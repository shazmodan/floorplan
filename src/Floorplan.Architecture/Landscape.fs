/// A folder of solutions as one landscape of systems (docs/landscape-plan.md): each solution a system, derived from
/// the solutions' analysis models as `C4` derives one solution's model from its analysis.
module Floorplan.Architecture.Landscape

open System
open System.IO
open Floorplan.Model
open Floorplan.Layout

/// A solution of the landscape as analyzed: its file, and its model when it could be analyzed.
type Analyzed =
    { /// The solution file, absolute.
      File: string
      Model: Result<AnalysisModel, string> }

/// A name as a folder and id: lower case, with anything but letters, digits, dots, dashes and underscores as "-".
let slugOf (name: string) =
    let chars =
        name.ToLowerInvariant()
        |> Seq.map (fun c -> if Char.IsAsciiLetterOrDigit c || c = '.' || c = '-' || c = '_' then c else '-')
        |> Seq.toArray

    match String(chars).Trim('-') with
    | "" -> "solution"
    | slug -> slug

/// Slugs for names, unique: a second "shop" is "shop-2".
let slugsOf (names: string list) =
    names
    |> List.mapFold
        (fun (taken: Set<string>) name ->
            let first = slugOf name
            let slug = Seq.initInfinite (fun i -> if i = 0 then first else $"{first}-{i + 1}") |> Seq.find (taken.Contains >> not)
            slug, taken.Add slug)
        Set.empty
    |> fst

/// The id of a solution's system.
let systemId (slug: string) = "system:" + slug

/// The exit points the website shows by default.
let private shown (model: AnalysisModel) (x: ExitPoint) =
    not (model.Presentation.HiddenCategories |> List.contains x.Category)
    && not (model.Presentation.HiddenConfidence |> List.contains x.Confidence)

/// A path relative to the analyzed folder, with forward slashes on every system.
let private relative (root: string) (path: string) =
    Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')

let private summary (root: string) (slug: string) (a: Analyzed) : LandscapeSolution =
    let path = relative root a.File
    let name = Path.GetFileNameWithoutExtension a.File

    match a.Model with
    | Error failure ->
        { Name = name
          Slug = slug
          Path = path
          Languages = []
          Frameworks = []
          Applications = 0
          Projects = 0
          EntryPoints = 0
          ExitPoints = 0
          Errors = 1
          Warnings = 0
          Failure = Some failure }
    | Ok m ->
        let projects = m.Projects |> List.filter (fun p -> not p.IsTestProject)
        let count severity = m.Diagnostics |> List.filter (fun d -> d.Severity = severity) |> List.length

        { Name = m.Solution.Name
          Slug = slug
          Path = path
          Languages = projects |> List.map (fun p -> p.Language) |> List.distinct |> List.sort
          Frameworks = projects |> List.map (fun p -> p.TargetFramework) |> List.distinct |> List.sort
          Applications = projects |> List.filter (fun p -> p.IsApplication) |> List.length
          Projects = projects.Length
          EntryPoints = m.EntryPoints.Length
          ExitPoints = m.ExitPoints |> List.filter (shown m) |> List.length
          Errors = count DiagnosticSeverity.Error
          Warnings = count DiagnosticSeverity.Warning
          Failure = None }

/// Every solution's errors and warnings, named after the solution, and why a solution could not be analyzed.
let private diagnosticsOf (solutions: (LandscapeSolution * Analyzed) list) =
    solutions
    |> List.collect (fun (s, a) ->
        match a.Model with
        | Error failure ->
            [ { Severity = DiagnosticSeverity.Error
                Code = "solution-failed"
                Message = $"[{s.Name}] {failure}"
                ProjectId = None
                Location = None } ]
        | Ok m ->
            m.Diagnostics
            |> List.filter (fun d -> d.Severity <> DiagnosticSeverity.Info)
            |> List.map (fun d -> { d with Message = $"[{s.Name}] {d.Message}" }))


let private warning (code: string) (message: string) =
    { Severity = DiagnosticSeverity.Warning
      Code = code
      Message = message
      ProjectId = None
      Location = None }

// ---------- The landscape file ----------

/// The optional file in the analyzed folder that says what the analysis cannot: which system a host is.
[<Literal>]
let FileName = "floorplan.landscape.json"

/// What the landscape file says: hosts (in lower case) and the system each one is, by its solution's name.
type LandscapeFile = { Hosts: Map<string, string> }

let noFile = { Hosts = Map.empty }

/// The landscape file of a folder, `{ "hosts": { "api.contoso.com": "SystemB" } }`, if it has one.
let readFile (root: string) : LandscapeFile * Diagnostic list =
    let file = Path.Combine(root, FileName)

    if not (File.Exists file) then
        noFile, []
    else
        try
            use doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText file)

            let hosts =
                match doc.RootElement.TryGetProperty "hosts" with
                | true, h when h.ValueKind = System.Text.Json.JsonValueKind.Object ->
                    [ for p in h.EnumerateObject() do
                          if p.Value.ValueKind = System.Text.Json.JsonValueKind.String then
                              yield p.Name.ToLowerInvariant(), p.Value.GetString() ]
                    |> Map.ofList
                | _ -> Map.empty

            { Hosts = hosts }, []
        with e ->
            noFile, [ warning "landscape-file.invalid" $"{FileName} could not be read, so it was not used: {e.Message}" ]

// ---------- Matching what one system sends to what another receives ----------

/// The type part of a member, "Namespace.Type::Method" -> "Namespace.Type", and its namespace.
let private namespaceOf (memberName: string) =
    let typeName = match memberName.IndexOf "::" with -1 -> memberName | i -> memberName.Substring(0, i)
    let outer = typeName.Split('/').[0]

    match outer.LastIndexOf '.' with
    | -1 -> ""
    | i -> outer.Substring(0, i)

/// The library a primitive or a registration belongs to, when it is neither the platform's nor the solution's own.
let private libraryOf (own: Set<string>) (memberName: string) =
    let ns = namespaceOf memberName
    if ns = "" || ns = "System" || ns.StartsWith "System." || own.Contains ns then None else Some ns

/// Two ends go through the same library (one broker) unless both are known and neither is part of the other.
let private sameLibrary (a: string option) (b: string option) =
    match a, b with
    | Some a, Some b -> a = b || a.StartsWith(b + ".") || b.StartsWith(a + ".")
    | _ -> true

let private isUrl (value: string) =
    value.StartsWith "http://" || value.StartsWith "https://"

/// The host of an absolute URL, in lower case and without its port, when the analysis knows all of it.
let hostOf (url: string) =
    if isUrl url then
        let rest = url.Substring(url.IndexOf "//" + 2)
        let authority = rest.Split([| '/'; '?'; '#' |]).[0]
        let host = authority.Split(':').[0]
        if host = "" || host.Contains "{" || host.Contains "…" then None else Some(host.ToLowerInvariant())
    else
        None

/// The path of a URL, without its scheme, host, query and fragment.
let pathOf (url: string) =
    let path =
        if isUrl url then
            let rest = url.Substring(url.IndexOf "//" + 2)

            match rest.IndexOf '/' with
            | -1 -> "/"
            | i -> rest.Substring i
        elif url.StartsWith "/" then
            url
        else
            "/" + url

    path.Split([| '?'; '#' |]).[0]

let private segments (path: string) =
    path.Split('/', StringSplitOptions.RemoveEmptyEntries) |> Array.toList

let private isParameter (segment: string) = segment.StartsWith "{" && segment.EndsWith "}"

/// A segment the caller only knows at run time: a template's hole.
let private isHole (segment: string) = segment.Contains "{" || segment.Contains "…"

/// Whether a call's path is one of a route's: a route parameter takes any segment, a segment only known at run time
/// matches only a parameter, and a route made only of parameters (a catch-all) names nothing, so matches nothing.
let routeMatches (path: string) (route: string) =
    let u = segments path
    let r = segments route

    let rec go (u: string list) (r: string list) =
        match u, r with
        | _, rs :: _ when rs.StartsWith "{*" -> true
        | [], rest -> rest |> List.forall (fun s -> s.EndsWith "?}")
        | _, [] -> false
        | _ :: us, rs :: rest when isParameter rs || rs = "*" -> go us rest
        | us :: _, _ when isHole us -> false
        | us :: ut, rs :: rest -> String.Equals(us, rs, StringComparison.OrdinalIgnoreCase) && go ut rest

    r |> List.exists (fun s -> not (isParameter s) && s <> "*") && go u r

/// Routes too common to say which system a call is for when nothing else does.
let private generic = set [ "/"; "/health"; "/healthz"; "/ping"; "/status"; "/ready"; "/live"; "/metrics" ]

/// The HTTP method of a call, when the member it calls names one (GetAsync, PostAsJsonAsync, DeleteAsync).
let methodOf (own: Set<string>) (x: ExitPoint) =
    // An interface the solution declares (Refit's) is named by the solution, not after the call it makes.
    if own.Contains(namespaceOf x.Primitive) then
        None
    else
        let name = match x.Primitive.IndexOf "::" with -1 -> x.Primitive | i -> x.Primitive.Substring(i + 2)

        [ "Get"; "Post"; "Put"; "Delete"; "Patch"; "Head"; "Options" ]
        |> List.tryFind (fun verb ->
            name.StartsWith(verb, StringComparison.Ordinal)
            && (name.Length = verb.Length || Char.IsUpper name[verb.Length]))
        |> Option.map (fun verb -> verb.ToUpperInvariant())

/// Letters and digits only, in lower case: how a host can name a system ("billing-api.internal" names Billing).
let private bare (s: string) =
    String(s.ToLowerInvariant() |> Seq.filter Char.IsAsciiLetterOrDigit |> Seq.toArray)

let private names (host: string) (name: string) =
    let n = bare name
    n.Length >= 3 && (bare host).Contains n

// ---------- The landscape's C4 model ----------

/// The id of a solution's container in the landscape.
let containerId (slug: string) (solutionContainer: string) =
    $"container:{slug}/{solutionContainer.Substring 10}"

/// What names a system of its own (a host, a library, a person) is one element in the landscape, which the systems
/// that use it share; anything else (the file system, "a database", "HTTP services whose address is not known")
/// is each system's own.
let private externalId (slug: string) (id: string) =
    if id.StartsWith "external:http:" || id.StartsWith "external:library:" || id.StartsWith "person:" then id
    else $"{id}@{slug}"

/// A solution of the landscape with what matching needs of it.
type private Part =
    { Slug: string
      Name: string
      Model: AnalysisModel
      Elements: Map<string, C4Element>
      Own: Set<string>
      /// A solution's element as the landscape shows it: a component or code element as its container.
      Lift: string -> string option }

let private partOf (slug: string) (model: AnalysisModel) : Part =
    let elements = model.Architecture.Elements |> List.map (fun e -> e.Id, e) |> Map.ofList

    let rec lift (id: string) =
        match elements.TryFind id with
        | Some e when e.Kind = C4Kind.Component || e.Kind = C4Kind.Code -> e.Parent |> Option.bind lift
        | Some e when e.Kind = C4Kind.Container -> Some(containerId slug e.Id)
        | Some e when e.Kind = C4Kind.SoftwareSystem -> Some(systemId slug)
        | Some e -> Some(externalId slug e.Id)
        | None -> None

    { Slug = slug
      Name = model.Solution.Name
      Model = model
      Elements = elements
      Own = model.Units |> List.map (fun u -> u.Name) |> Set.ofList
      Lift = lift }

/// A relationship as found, before those with the same ends and description are merged.
type private Link =
    { From: string
      To: string
      Description: string
      Technology: string option
      Sources: string list }

let private merge (links: Link list) : C4Relationship list =
    links
    |> List.groupBy (fun l -> l.From, l.To, l.Description)
    |> List.map (fun ((from, target, description), ls) ->
        let technologies =
            ls |> List.choose (fun l -> l.Technology) |> List.collect (fun t -> t.Split(", ") |> List.ofArray) |> List.distinct |> List.sort

        ({ From = from
           To = target
           Description = description
           Technology = (if technologies.IsEmpty then None else Some(String.Join(", ", technologies)))
           Sources = ls |> List.collect (fun l -> l.Sources) |> List.distinct |> List.sort }
        : C4Relationship))
    |> List.sortBy (fun r -> r.From, r.To, r.Description)

/// The calls of one system that another one receives (docs/landscape-plan.md, "Matching"): the interaction, and the
/// exit point's id in its solution.
let private matchAll (file: LandscapeFile) (parts: Part list) : (Interaction * string) list =
    let bySlug = parts |> List.map (fun p -> p.Slug, p) |> Map.ofList
    let fileSystem = parts |> List.map (fun p -> p.Name.ToLowerInvariant(), p.Slug) |> Map.ofList

    // The containers an exit point belongs to: those its solution shows reaching an external system with it. A call
    // the solution already matched to one of its own applications is not the landscape's.
    let callers (p: Part) (x: ExitPoint) =
        p.Model.Architecture.Relationships
        |> List.filter (fun r -> r.Sources |> List.contains x.Id)
        |> List.choose (fun r ->
            match p.Lift r.From, p.Lift r.To with
            | Some f, Some t when f.StartsWith "container:" && not (t.StartsWith "container:" || t.StartsWith "system:") -> Some(f, r.Technology)
            | _ -> None)
        |> List.distinctBy fst

    let interaction (p: Part) (x: ExitPoint) (q: Part) (e: EntryPoint) (called: string) (description: string) (methods: string list) (matchedBy: string) =
        // The solution drops a technology that only repeats the external system's name (RabbitMQ.Client to
        // RabbitMQ.Client); between two systems it says how they talk.
        let through = if x.Category = EffectCategory.Network then libraryOf p.Own x.Primitive else None

        [ for from, technology in callers p x ->
              { FromSystem = systemId p.Slug
                FromContainer = from
                FromMember = x.Member
                ToSystem = systemId q.Slug
                ToContainer = containerId q.Slug ("container:" + e.ApplicationId)
                ToMember = e.HandlerMember
                What = defaultArg e.Route ""
                Methods = methods
                Called = called
                Description = description
                Technology = technology |> Option.orElse through
                MatchedBy = matchedBy
                Probable = (matchedBy = "route-only")
                Sources = [ $"{p.Slug}:{x.Id}"; $"{q.Slug}:{e.Id}" ] },
              x.Id ]

    // The most specific of a system's routes a path matches first: fewer catch-alls, more literal segments; and an
    // entry point with a handler of its own before one whose response a library builds.
    let specificity (e: EntryPoint) =
        let route = defaultArg e.Route ""
        let segs = segments route
        (segs |> List.filter (fun s -> s.StartsWith "{*") |> List.length),
        -(segs |> List.filter (fun s -> not (isParameter s)) |> List.length),
        e.HandlerMember.StartsWith " ",
        route,
        e.ApplicationId

    [ for p in parts do
          for x in p.Model.ExitPoints |> List.filter (shown p.Model) do
              match x.Category, x.Payload with
              | EffectCategory.Network, Some { Kind = PayloadKind.Literal; Value = Some address } ->
                  let sent = libraryOf p.Own x.Primitive

                  for q in parts do
                      if q.Slug <> p.Slug then
                          for e in q.Model.EntryPoints do
                              let received = e.RegisteredThrough |> Option.bind (libraryOf q.Own)

                              if e.Category = EffectCategory.Network && e.Route = Some address && sameLibrary sent received then
                                  yield! interaction p x q e address $"sends messages to {address}" [] "queue"
              | EffectCategory.HttpOut, Some { Kind = (PayloadKind.Literal | PayloadKind.Template); Value = Some url } ->
                  let path = pathOf url
                  let host = hostOf url
                  let calledAs = methodOf p.Own x

                  let receives (q: Part) =
                      q.Model.EntryPoints
                      |> List.filter (fun e ->
                          e.Category = EffectCategory.HttpIn
                          && e.Route |> Option.exists (routeMatches path)
                          && (calledAs.IsNone || e.HttpMethod.IsNone || e.HttpMethod = calledAs))
                      |> List.sortBy specificity

                  let candidates =
                      parts |> List.filter (fun q -> q.Slug <> p.Slug) |> List.map (fun q -> q, receives q) |> List.filter (snd >> List.isEmpty >> not)

                  let methods (q: Part) (e: EntryPoint) =
                      match calledAs with
                      | Some m -> [ m ]
                      | None ->
                          q.Model.EntryPoints
                          |> List.filter (fun o -> o.ApplicationId = e.ApplicationId && o.Route = e.Route)
                          |> List.choose (fun o -> o.HttpMethod)
                          |> List.distinct
                          |> List.sort

                  let link (q: Part) (es: EntryPoint list) (matchedBy: string) =
                      let e = es.Head
                      interaction p x q e url $"sends HTTP requests to {defaultArg e.Route path}" (methods q e) matchedBy

                  let mapped = host |> Option.bind file.Hosts.TryFind

                  let named =
                      candidates
                      |> List.filter (fun (q, es) ->
                          host |> Option.exists (fun h -> names h q.Name || es |> List.exists (fun e -> names h e.ApplicationId)))

                  match mapped with
                  | Some system ->
                      // The file decides: the call goes to that system's route, and nowhere else.
                      let slug = fileSystem.TryFind(system.ToLowerInvariant()) |> Option.defaultValue (system.ToLowerInvariant())

                      match candidates |> List.tryFind (fun (q, _) -> q.Slug = slug) with
                      | Some(q, es) -> yield! link q es "landscape-file"
                      | None -> ()
                  | None when not named.IsEmpty ->
                      for q, es in named do
                          yield! link q es "host"
                  | None ->
                      let own = receives p

                      let common = generic.Contains(match path.ToLowerInvariant().TrimEnd '/' with "" -> "/" | p -> p)

                      match candidates with
                      | [ q, es ] when own.IsEmpty && not common ->
                          yield! link q es "route-only"
                      | _ -> ()
              | _ -> () ]

/// What a source of the landscape is, as its solution's site shows it.
let private sourceOf (p: Part) : Map<string, LandscapeSource> =
    let label (e: EntryPoint) =
        match e.HttpMethod, e.Route with
        | Some m, Some r -> $"{m} {r}"
        | None, Some r -> r
        | _, None -> e.HandlerMember

    let category (c: EffectCategory) = Json.unionName c

    (p.Model.EntryPoints
     |> List.map (fun e -> { Id = $"{p.Slug}:{e.Id}"; Label = label e; Page = $"#/evidence/entry:{e.Id}" }))
    @ (p.Model.ExitPoints
       |> List.map (fun x ->
           let what = x.Payload |> Option.bind (fun v -> v.Value) |> Option.defaultValue x.Primitive
           { Id = $"{p.Slug}:{x.Id}"; Label = $"{category x.Category}: {what}"; Page = $"#/evidence/exit:{x.Id}" }))
    @ (p.Model.Projects |> List.map (fun pr -> { Id = $"{p.Slug}:{pr.Id}"; Label = pr.Name; Page = $"#/units?project={Uri.EscapeDataString pr.Id}" }))
    |> List.map (fun s -> s.Id, s)
    |> Map.ofList

/// The landscape's elements and relationships, and the interactions between its systems.
let private architectureOf (file: LandscapeFile) (parts: Part list) =
    let matches = matchAll file parts
    let matched = matches |> List.map (fun (i, _) -> i.Sources.Head) |> Set.ofList

    let systems =
        parts
        |> List.map (fun p ->
            let description =
                p.Elements.TryFind C4.SystemId |> Option.map (fun e -> e.Description) |> Option.defaultValue $"The {p.Name} solution"

            { Id = systemId p.Slug
              Kind = C4Kind.SoftwareSystem
              Name = p.Name
              Technology = None
              Description = description
              Parent = None
              Tags = []
              Sources = p.Model.Projects |> List.map (fun pr -> $"{p.Slug}:{pr.Id}") })

    let containers =
        [ for p in parts do
              for e in p.Model.Architecture.Elements do
                  if e.Kind = C4Kind.Container then
                      yield
                          { e with
                              Id = containerId p.Slug e.Id
                              Parent = Some(systemId p.Slug)
                              Sources = e.Sources |> List.map (fun s -> $"{p.Slug}:{s}") } ]

    let outside =
        [ for p in parts do
              for e in p.Model.Architecture.Elements do
                  if e.Kind = C4Kind.Person || e.Kind = C4Kind.ExternalSystem then
                      let id = externalId p.Slug e.Id
                      // One of a system's own, named with it: "File system (Billing)".
                      let name = if id = e.Id then e.Name else $"{e.Name} ({p.Name})"
                      yield { e with Id = id; Name = name; Sources = e.Sources |> List.map (fun s -> $"{p.Slug}:{s}") } ]
        |> List.groupBy (fun e -> e.Id)
        |> List.map (fun (_, es) -> { es.Head with Sources = es |> List.collect (fun e -> e.Sources) |> List.distinct |> List.sort })

    // Each solution's relationships between the elements the landscape shows, without the calls matched to another
    // system, which go there instead of to an external system.
    let within =
        [ for p in parts do
              for r in p.Model.Architecture.Relationships do
                  match p.Lift r.From, p.Lift r.To with
                  | Some f, Some t when f <> t ->
                      let sources = r.Sources |> List.map (fun s -> $"{p.Slug}:{s}")
                      let toExternal = not (t.StartsWith "container:" || t.StartsWith "system:")
                      let left = if toExternal then sources |> List.filter (matched.Contains >> not) else sources

                      if not left.IsEmpty then
                          yield
                              { From = f
                                To = t
                                Description = r.Description
                                Technology = r.Technology
                                Sources = left }
                  | _ -> () ]

    let between =
        matches
        |> List.map (fun (i, _) ->
            { From = i.FromContainer
              To = i.ToContainer
              Description = i.Description
              Technology = i.Technology
              Sources = i.Sources })

    let interactions =
        matches
        |> List.map fst
        |> List.distinct
        |> List.sortBy (fun i -> i.FromSystem, i.FromContainer, i.ToSystem, i.ToContainer, i.What, i.FromMember)

    // An external system the landscape no longer shows anything reaching (the host of a call matched to a system).
    let relationships = merge (within @ between)
    let touched = relationships |> List.collect (fun r -> [ r.From; r.To ]) |> Set.ofList
    let outside = outside |> List.filter (fun e -> touched.Contains e.Id) |> List.sortBy (fun e -> e.Id)
    systems @ containers @ outside, relationships, interactions

// ---------- Views ----------

/// The scope of the views of the whole landscape, which is no element of the model.
[<Literal>]
let LandscapeScope = "landscape"

/// The relationships as a view shows them: each end moved up to the shown element that contains it (a container
/// to its system), those within one shown element dropped, and the rest merged.
let private liftedTo (byId: Map<string, C4Element>) (shown: string list) (relationships: C4Relationship list) =
    let shownSet = Set.ofList shown

    let rec within (id: string) (guard: int) =
        if shownSet.Contains id then Some id
        elif guard > 8 then None
        else byId.TryFind id |> Option.bind (fun e -> e.Parent) |> Option.bind (fun p -> within p (guard + 1))

    relationships
    |> List.choose (fun r ->
        match within r.From 0, within r.To 0 with
        | Some f, Some t when f <> t ->
            Some
                { From = f
                  To = t
                  Description = r.Description
                  Technology = r.Technology
                  Sources = r.Sources }
        | _ -> None)
    |> merge

/// The containers of every system, each system a box around its own in rows of at most three, laid out as one
/// graph with the people and external systems around them; relationships are drawn straight between the boxes.
let private grouped (view: C4View) (byId: Map<string, C4Element>) : GraphLayout =
    let gap, pad, head = 48.0, 28.0, 30.0
    let isContainer (id: string) = byId.TryFind id |> Option.exists (fun e -> e.Kind = C4Kind.Container)
    let groupOf (id: string) = if isContainer id then byId[id].Parent.Value else id

    let groups =
        view.Elements |> List.filter isContainer |> List.groupBy groupOf |> List.map (fun (g, cs) -> g, cs) |> Map.ofList

    let others = view.Elements |> List.filter (isContainer >> not)
    let columns (n: int) = min n 3

    let size (n: int) =
        let c = columns n
        let rows = (n + c - 1) / c
        float c * C4.boxWidth + float (c - 1) * gap + 2.0 * pad, float rows * C4.boxHeight + float (rows - 1) * gap + 2.0 * pad + head

    let pairs =
        view.Relationships |> List.map (fun r -> groupOf r.From, groupOf r.To) |> List.filter (fun (a, b) -> a <> b) |> List.distinct

    let ids = (groups |> Map.toList |> List.map fst) @ others
    let closing = C4.closingCycles ids pairs

    let graph =
        Layered.layout
            { Layered.defaults with LayerGap = 110.0; NodeGap = 48.0 }
            ((groups |> Map.toList |> List.map (fun (g, cs) -> let w, h = size cs.Length in Layered.node g w h))
             @ (others |> List.map (fun id -> Layered.node id C4.boxWidth C4.boxHeight)))
            (pairs |> List.filter (closing.Contains >> not) |> List.map (fun (a, b) -> Layered.edge a b))

    let placed =
        graph.Nodes
        |> List.collect (fun n ->
            match groups.TryFind n.Id with
            | Some cs ->
                let c = columns cs.Length

                cs
                |> List.mapi (fun i id ->
                    { n with
                        Id = id
                        X = n.X + pad + float (i % c) * (C4.boxWidth + gap)
                        Y = n.Y + pad + head + float (i / c) * (C4.boxHeight + gap)
                        Width = C4.boxWidth
                        Height = C4.boxHeight
                        Members = [] })
            | None -> [ n ])

    let at = placed |> List.map (fun n -> n.Id, n) |> Map.ofList

    let edges =
        view.Relationships
        |> List.map (fun r -> r.From, r.To)
        |> List.distinct
        |> List.choose (fun (a, b) ->
            match at.TryFind a, at.TryFind b with
            | Some s, Some t ->
                let points =
                    if t.Y > s.Y + s.Height then
                        [ { X = s.X + s.Width / 2.0; Y = s.Y + s.Height }; { X = t.X + t.Width / 2.0; Y = t.Y } ]
                    elif s.Y > t.Y + t.Height then
                        [ { X = s.X + s.Width / 2.0; Y = s.Y }; { X = t.X + t.Width / 2.0; Y = t.Y + t.Height } ]
                    elif t.X > s.X then
                        [ { X = s.X + s.Width; Y = s.Y + s.Height / 2.0 }; { X = t.X; Y = t.Y + t.Height / 2.0 } ]
                    else
                        [ { X = s.X; Y = s.Y + s.Height / 2.0 }; { X = t.X + t.Width; Y = t.Y + t.Height / 2.0 } ]

                Some { From = a; To = b; Points = points }
            | _ -> None)

    { graph with Nodes = placed; Edges = edges }

/// The landscape's views (docs/landscape-plan.md, "Views"): every system with the people and the external systems
/// more than one of them uses, the same with every external system, each system's containers with the containers
/// and systems they talk to, and every container grouped in its system.
let private viewsOf (elements: C4Element list) (relationships: C4Relationship list) : C4View list =
    let byId = elements |> List.map (fun e -> e.Id, e) |> Map.ofList
    let ofKind (kinds: C4Kind list) = elements |> List.filter (fun e -> List.contains e.Kind kinds) |> List.map (fun e -> e.Id)
    let systems = ofKind [ C4Kind.SoftwareSystem ]
    let containers = ofKind [ C4Kind.Container ]
    let outside = ofKind [ C4Kind.Person; C4Kind.ExternalSystem ]

    let systemOf (id: string) =
        match byId.TryFind id with
        | Some e when e.Kind = C4Kind.Container -> e.Parent
        | Some e when e.Kind = C4Kind.SoftwareSystem -> Some id
        | _ -> None

    let usedBy (id: string) =
        relationships
        |> List.choose (fun r -> if r.To = id then systemOf r.From elif r.From = id then systemOf r.To else None)
        |> List.distinct

    let shared = outside |> List.filter (fun o -> (usedBy o).Length >= 2)

    let view (id: string) (level: C4Level) (scope: string) (shown: string list) (parts: Set<string>) =
        let v =
            { Id = id
              Level = level
              Scope = scope
              Elements = shown
              Relationships = liftedTo byId shown relationships |> List.filter (fun r -> parts.Contains r.From || parts.Contains r.To)
              Layout = AnalysisModel.emptyGraph }

        { v with Layout = C4.laidOut v parts }

    let ofSystem (system: string) =
        let mine = containers |> List.filter (fun c -> byId[c].Parent = Some system)
        let mineSet = Set.ofList mine
        let around = (containers |> List.filter (mineSet.Contains >> not)) @ outside
        let all = liftedTo byId (mine @ around) relationships

        let related =
            around |> List.filter (fun a -> all |> List.exists (fun r -> (r.From = a && mineSet.Contains r.To) || (r.To = a && mineSet.Contains r.From)))

        view $"containers:{system.Substring 7}" C4Level.Containers system (mine @ related) mineSet

    let every =
        let shown = containers @ shared
        let shownSet = Set.ofList containers

        let v =
            { Id = "containers"
              Level = C4Level.Containers
              Scope = LandscapeScope
              Elements = shown
              Relationships = liftedTo byId shown relationships |> List.filter (fun r -> shownSet.Contains r.From || shownSet.Contains r.To)
              Layout = AnalysisModel.emptyGraph }

        { v with Layout = grouped v byId }

    let touched = relationships |> List.collect (fun r -> [ r.From; r.To ]) |> Set.ofList

    [ view "context" C4Level.Context LandscapeScope (systems @ shared) (Set.ofList systems)
      view "context-all" C4Level.Context LandscapeScope (systems @ (outside |> List.filter touched.Contains)) (Set.ofList systems)
      every ]
    @ (systems |> List.filter (fun s -> containers |> List.exists (fun c -> byId[c].Parent = Some s)) |> List.map ofSystem)

/// The landscape of the solutions found in a folder.
let derive
    (toolVersion: string)
    (generatedAt: string)
    (root: string)
    (file: LandscapeFile)
    (nested: string list)
    (analyzed: Analyzed list)
    : Landscape =
    let root = Path.GetFullPath root
    let slugs = slugsOf (analyzed |> List.map (fun a -> Path.GetFileNameWithoutExtension a.File))
    let solutions = List.zip slugs analyzed |> List.map (fun (slug, a) -> summary root slug a, a)

    let parts =
        solutions |> List.choose (fun (s, a) -> a.Model |> Result.toOption |> Option.map (partOf s.Slug))

    let elements, relationships, interactions = architectureOf file parts

    // The file names systems by their solution's name: one it does not know is a mistake worth saying.
    let unknown =
        file.Hosts
        |> Map.toList
        |> List.filter (fun (_, system) -> not (solutions |> List.exists (fun (s, _) -> s.Name.ToLowerInvariant() = system.ToLowerInvariant() || s.Slug = system.ToLowerInvariant())))
        |> List.map (fun (host, system) ->
            warning "landscape-file.unknown-system" $"{FileName} maps {host} to {system}, which is no solution of this folder.")

    let referenced =
        (relationships |> List.collect (fun r -> r.Sources)) @ (interactions |> List.collect (fun i -> i.Sources)) |> Set.ofList

    let sources =
        parts
        |> List.collect (fun p -> sourceOf p |> Map.toList |> List.map snd)
        |> List.filter (fun s -> referenced.Contains s.Id)
        |> List.sortBy (fun s -> s.Id)

    { SchemaVersion = AnalysisModel.CurrentSchemaVersion
      ToolVersion = toolVersion
      GeneratedAt = generatedAt
      Name = (match Path.GetFileName(root.TrimEnd('\\', '/')) with "" -> root | n -> n)
      Path = root
      Solutions = solutions |> List.map fst
      Nested = nested |> List.map (relative root)
      Architecture =
        { Elements = elements
          Relationships = relationships
          Views = viewsOf elements relationships }
      Interactions = interactions
      Sources = sources
      Diagnostics = diagnosticsOf solutions @ unknown }

/// The landscape as text, for people and golden files: its solutions, its C4 model, and its interactions.
let describe (l: Landscape) : string list =
    [ for s in l.Solutions do
          let failure = if s.Failure.IsSome then " | failed" else ""
          yield $"solution {s.Slug} | {s.Name} | {s.Path} | {s.Applications} applications, {s.Projects} projects, {s.EntryPoints} entry points, {s.ExitPoints} exit points{failure}"
      for n in l.Nested do
          yield $"nested {n}"
      yield! C4.describe l.Architecture
      for i in l.Interactions do
          let methods = if i.Methods.IsEmpty then "-" else String.Join(",", i.Methods)
          let probable = if i.Probable then " (probable)" else ""
          let sources = String.concat " " i.Sources
          yield
              $"interaction {i.FromContainer} -> {i.ToContainer} | {i.What} | {methods} | called as {i.Called} | {i.MatchedBy}{probable} | {sources}" ]
