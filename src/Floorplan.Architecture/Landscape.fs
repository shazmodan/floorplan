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

/// The folder of an absolute path the analysis knows (a template's up to its first hole), with forward slashes: a
/// file's folder, or the path itself when it is a folder (a watched one). A relative path depends on the working
/// folder of each process, so it names no folder two systems could share.
let folderOf (isFolder: bool) (path: string) =
    let p = path.Replace('\\', '/')
    let known = match p.IndexOfAny [| '{'; '…' |] with -1 -> p | i -> p.Substring(0, i)
    let drive = known.Length >= 3 && Char.IsAsciiLetter known[0] && known[1] = ':' && known[2] = '/'

    if not (drive || known.StartsWith "/") then
        None
    else
        let folder =
            if isFolder && known.Length = p.Length then known.TrimEnd '/'
            else match known.LastIndexOf '/' with i when i > 0 -> known.Substring(0, i) | _ -> ""

        if folder = "" || folder.EndsWith ":" || folder = "/" then None
        // A Windows path is the same in any case.
        elif drive then Some(folder.ToLowerInvariant(), folder)
        else Some(folder, folder)

/// The folders more than one system uses (docs/landscape-plan.md, "Matching", step 5): files one system writes and
/// another reads or watches. Each source ("<slug>:<id>", an exit point or a watched folder's entry point) that uses
/// one, with its element's id and the folder's name.
let private sharedFolders (parts: Part list) : Map<string, string * string> =
    let uses =
        [ for p in parts do
              for x in p.Model.ExitPoints |> List.filter (shown p.Model) do
                  match x.Category, x.Payload with
                  | (EffectCategory.FilesystemRead | EffectCategory.FilesystemWrite), Some { Kind = (PayloadKind.Literal | PayloadKind.Template); Value = Some path } ->
                      match folderOf false path with
                      | Some(key, name) -> yield key, name, p.Slug, $"{p.Slug}:{x.Id}"
                      | None -> ()
                  | _ -> ()

              for e in p.Model.EntryPoints do
                  match e.Category, e.Route with
                  | (EffectCategory.FilesystemRead | EffectCategory.FilesystemWrite), Some folder ->
                      match folderOf true folder with
                      | Some(key, name) -> yield key, name, p.Slug, $"{p.Slug}:{e.Id}"
                      | None -> ()
                  | _ -> () ]

    uses
    |> List.groupBy (fun (key, _, _, _) -> key)
    |> List.filter (fun (_, us) -> (us |> List.map (fun (_, _, slug, _) -> slug) |> List.distinct).Length >= 2)
    |> List.collect (fun (key, us) ->
        let _, name, _, _ = us.Head
        us |> List.map (fun (_, _, _, source) -> source, ($"external:folder:{key}", name)))
    |> Map.ofList

/// The databases more than one system uses (docs/landscape-plan.md, "Matching", step 5): the same server and
/// database in the connection strings of their connections (ConnectionString.database). Each source that uses one,
/// with its element's id and the database's name: the connections opened to it, and every other database use of a
/// project whose connections all name that one database.
let private sharedDatabases (parts: Part list) : Map<string, string * string> =
    let projectOf (p: Part) =
        let units = p.Model.Units |> List.map (fun u -> u.Id, u.ProjectId) |> Map.ofList
        fun (x: ExitPoint) -> units.TryFind x.Unit

    // Each known connection: its database, the solution, the project and the source.
    let connections =
        [ for p in parts do
              let project = projectOf p

              for x in p.Model.ExitPoints |> List.filter (shown p.Model) do
                  match x.Category, x.Payload, x.Connection with
                  | EffectCategory.Database, Some { Role = "connection"; Kind = PayloadKind.Literal; Value = Some text }, _
                  | EffectCategory.Database, _, Some text ->
                      match ConnectionString.database text with
                      | Some(key, name) -> yield key, name, p.Slug, project x, $"{p.Slug}:{x.Id}"
                      | None -> ()
                  | _ -> () ]

    let shared =
        connections
        |> List.groupBy (fun (key, _, _, _, _) -> key)
        |> List.filter (fun (_, cs) -> (cs |> List.map (fun (_, _, slug, _, _) -> slug) |> List.distinct).Length >= 2)
        |> List.map (fun (key, cs) ->
            let _, name, _, _, _ = cs.Head
            key, ($"external:database:{key}", name))
        |> Map.ofList

    // A project whose connections all name one shared database: its other database uses go there as well. A
    // project with no connection of its own (a library whose Entity Framework Core context the application
    // registers) does the same with its solution's connections.
    let onlyOne (cs: (string * string * string * string option * string) list) =
        match cs |> List.map (fun (key, _, _, _, _) -> key) |> List.distinct with
        | [ key ] when shared.ContainsKey key -> Some shared[key]
        | _ -> None

    let only =
        connections
        |> List.groupBy (fun (_, _, slug, project, _) -> slug, project)
        |> List.choose (fun ((slug, project), cs) ->
            match project, onlyOne cs with
            | Some project, Some element -> Some((slug, project), element)
            | _ -> None)
        |> Map.ofList

    // Every connection made, whether or not its database is known: the solution, its project and its Entity
    // Framework Core context. A dependency's call that opens its own (Dapper's) is one.
    let made =
        [ for p in parts do
              let project = projectOf p

              for x in p.Model.ExitPoints |> List.filter (shown p.Model) do
                  match x.Category, x.Payload, x.Connection with
                  | EffectCategory.Database, Some { Role = "connection" }, _
                  | EffectCategory.Database, _, Some _ -> yield p.Slug, project x, x.Context
                  | _ -> () ]

    // Projects that make a connection of their own.
    let connected =
        made |> List.choose (fun (slug, project, _) -> project |> Option.map (fun pr -> slug, pr)) |> Set.ofList

    // A context given connection strings that all name one shared database: the calls made on it go there, whatever
    // other databases its project or solution connects to. A context given another (or one not known) has its calls
    // go to no shared database.
    let contextOf =
        let ids = parts |> List.map (fun p -> p.Slug, p.Model.ExitPoints |> List.map (fun x -> $"{p.Slug}:{x.Id}", x.Context) |> Map.ofList) |> Map.ofList
        let contextOfSource (slug: string) (source: string) = ids[slug].TryFind source |> Option.flatten

        let known =
            connections
            |> List.choose (fun ((_, _, slug, _, source) as c) -> contextOfSource slug source |> Option.map (fun context -> (slug, context), c))
            |> List.groupBy fst
            |> List.map (fun (k, cs) -> k, onlyOne (List.map snd cs))
            |> Map.ofList

        made
        |> List.choose (fun (slug, _, context) -> context |> Option.map (fun c -> slug, c))
        |> List.distinct
        |> List.map (fun k -> k, known.TryFind k |> Option.flatten)
        |> Map.ofList

    let solutionOnly =
        connections
        |> List.groupBy (fun (_, _, slug, _, _) -> slug)
        |> List.choose (fun (slug, cs) -> onlyOne cs |> Option.map (fun element -> slug, element))
        |> Map.ofList

    let databaseOf slug project =
        match only.TryFind(slug, project) with
        | Some element -> Some element
        | None when not (connected.Contains(slug, project)) -> solutionOnly.TryFind slug
        | None -> None

    [ for (key, _, _, _, source) in connections do
          match shared.TryFind key with
          | Some element -> yield source, element
          | None -> ()

      for p in parts do
          let project = projectOf p

          for x in p.Model.ExitPoints |> List.filter (shown p.Model) do
              // A call that opens its own connection (Dapper's) goes where that connection goes, above.
              if x.Category = EffectCategory.Database && x.Connection.IsNone then
                  let element =
                      match x.Context |> Option.bind (fun c -> contextOf.TryFind(p.Slug, c)) with
                      | Some ofContext -> ofContext
                      | None -> project x |> Option.bind (databaseOf p.Slug)

                  match element with
                  | Some element -> yield $"{p.Slug}:{x.Id}", element
                  | None -> () ]
    |> Map.ofList

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
    let folders = sharedFolders parts
    let databases = sharedDatabases parts

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

    // A folder or database two systems use is one element, which both reach instead of their own file system or
    // database.
    let sharedOf (uses: Map<string, string * string>) (description: string) (tag: string) =
        uses
        |> Map.toList
        |> List.groupBy (fun (_, (id, _)) -> id)
        |> List.map (fun (id, uses) ->
            { Id = id
              Kind = C4Kind.ExternalSystem
              Name = snd (snd uses.Head)
              Technology = None
              Description = description
              Parent = None
              Tags = [ tag ]
              Sources = uses |> List.map fst |> List.sort })

    let shared =
        sharedOf folders "A folder more than one system uses" "shared folder"
        @ sharedOf databases "A database more than one system uses" "shared database"

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
        |> List.append shared

    // Each solution's relationships between the elements the landscape shows, without the calls matched to another
    // system, which go there instead of to an external system.
    let within =
        [ for p in parts do
              let exits = p.Model.ExitPoints |> List.map (fun x -> $"{p.Slug}:{x.Id}", x) |> Map.ofList
              let projects = p.Model.Projects |> List.map (fun pr -> pr.Name) |> Set.ofList

              // The libraries a part of a relationship's calls go through, when the landscape sends that part
              // elsewhere (one shared database of several): the relationship's own says how all of them are made.
              let technologyOf (sources: string list) (whole: string option) =
                  match sources |> List.map exits.TryFind with
                  | found when found |> List.forall Option.isSome ->
                      let libraries =
                          found
                          |> List.choose (Option.bind (fun x -> x.ViaAssembly |> Option.filter (projects.Contains >> not)))
                          |> List.distinct
                          |> List.sort

                      match libraries with
                      | [] -> if found |> List.exists (fun x -> x.Value.Category = EffectCategory.HttpOut) then Some "HTTP" else None
                      | ls -> Some(String.Join(", ", ls))
                  | _ -> whole

              for r in p.Model.Architecture.Relationships do
                  match p.Lift r.From, p.Lift r.To with
                  | Some f, Some t when f <> t ->
                      let sources = r.Sources |> List.map (fun s -> $"{p.Slug}:{s}")
                      let toExternal = not (t.StartsWith "container:" || t.StartsWith "system:")
                      let left = if toExternal then sources |> List.filter (matched.Contains >> not) else sources

                      // A use of a shared folder goes to the folder, not to the system's own file system, and one of
                      // a shared database to the database, not to the database or library the system reaches.
                      let own = $"external:filesystem@{p.Slug}"
                      let at (id: string) (source: string) =
                          if id = own then folders.TryFind source |> Option.map fst |> Option.defaultValue id
                          elif id.StartsWith "external:" then databases.TryFind source |> Option.map fst |> Option.defaultValue id
                          else id

                      let groups = left |> List.groupBy (fun s -> at f s, at t s)

                      for (gf, gt), sources in groups do
                          yield
                              { From = gf
                                To = gt
                                Description = r.Description
                                Technology =
                                    if groups.Length = 1 && (gf, gt) = (f, t) then r.Technology
                                    else technologyOf sources r.Technology
                                Sources = sources }
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

/// What the landscape found about one of its solutions, for that solution's own C4 model: its calls other systems
/// receive, and its entry points other systems call.
let knownOf (l: Landscape) (slug: string) : C4.Known =
    let other (system: string) : C4.OtherSystem =
        let s = system.Substring 7
        let name = l.Solutions |> List.tryFind (fun x -> x.Slug = s) |> Option.map (fun x -> x.Name) |> Option.defaultValue s
        { Slug = s; Name = name }

    let idIn (prefix: string) (source: string) =
        if source.StartsWith(prefix + ":") then Some(source.Substring(prefix.Length + 1)) else None

    let sends =
        l.Interactions
        |> List.filter (fun i -> i.FromSystem = systemId slug)
        |> List.choose (fun i -> idIn slug i.Sources.Head |> Option.map (fun x -> x, (other i.ToSystem, i.Description)))
        |> List.distinctBy fst
        |> Map.ofList

    let receives =
        l.Interactions
        |> List.filter (fun i -> i.ToSystem = systemId slug)
        |> List.choose (fun i -> idIn slug (List.last i.Sources) |> Option.map (fun e -> e, other i.FromSystem))
        |> List.groupBy fst
        |> List.map (fun (e, os) -> e, os |> List.map snd |> List.distinct)
        |> Map.ofList

    { Sends = sends; Receives = receives }
