/// Reads catalogs. Each later catalog is a delta on an earlier one: { "extends": "net8.0", "add": [...],
/// "remove": [...] } (plan section 4.4). Users add rules with --catalog.
module Floorplan.Catalog.CatalogLoader

open System
open System.IO
open System.Reflection
open System.Text.Json
open Floorplan.Model

exception CatalogError of string

let private fail message = raise (CatalogError message)

let private str (e: JsonElement) (name: string) =
    match e.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString())
    | _ -> None

let private required (e: JsonElement) (name: string) (context: string) =
    match str e name with
    | Some v -> v
    | None -> fail $"{context}: missing \"{name}\"."

let private strings (e: JsonElement) (name: string) =
    match e.TryGetProperty name with
    | true, v when v.ValueKind = JsonValueKind.Array -> [ for x in v.EnumerateArray() -> x.GetString() ]
    | true, v when v.ValueKind = JsonValueKind.String -> [ v.GetString() ]
    | _ -> []

let private kind (text: string) context =
    match text with
    | "sink" -> Sink
    | "source" -> Source
    | "registration" -> Registration
    | "blind-spot" -> BlindSpot
    | "comparison" -> RuleKind.Comparison
    | "callback" -> Callback
    | "metadata" -> Metadata
    | "constant" -> Constant
    | "logger" -> Logger
    | "query" -> Query
    | "configured" -> Configured
    | "self-log" -> SelfLog
    | other -> fail $"{context}: unknown kind \"{other}\"."

let private requestPart (text: string) context =
    match text with
    | "method" -> RequestPart.Method
    | "path" -> RequestPart.Path
    | "host" -> RequestPart.Host
    | "query" -> RequestPart.Query
    | "header" -> RequestPart.Header
    | "body" -> RequestPart.Body
    | other -> fail $"{context}: unknown requestPart \"{other}\"."

let private parseRule (source: string) (e: JsonElement) : Rule =
    let id = required e "id" $"{source}: rule"
    let context = $"{source}: rule {id}"
    let k = kind (required e "kind" context) context

    let category =
        match str e "category" with
        | Some c ->
            match EffectCategory.tryParse c with
            | Some c -> Some c
            | None -> fail $"{context}: unknown category \"{c}\"."
        | None when k = Sink || k = Source || k = Query -> fail $"{context}: sinks, sources and queries need a category."
        | None -> None

    let members = strings e "members"

    if members.IsEmpty && k <> Configured then
        fail $"{context}: no members."

    let configuration =
        match e.TryGetProperty "configuration" with
        | true, c when c.ValueKind = JsonValueKind.Object ->
            if k <> Configured then
                fail $"{context}: only configured rules have a configuration."

            let elements = strings c "elements"

            if elements.IsEmpty then
                fail $"{context}: a configuration names its elements."

            Some
                { Root = required c "root" context
                  Elements = elements
                  TypeAttribute = required c "typeAttribute" context
                  Alias = str c "alias" }
        | _ when k = Configured -> fail $"{context}: configured rules need a configuration."
        | _ -> None

    let dispatch = str e "dispatch"

    match dispatch with
    | Some _ when k <> Logger -> fail $"{context}: only loggers dispatch."
    | Some d when not (d.Contains "::") -> fail $"{context}: dispatch names a member as \"Type::Member\"."
    | _ -> ()

    let logCall =
        match str e "logCall" with
        | None
        | Some "direct" -> LogCall.Direct
        | Some _ when k <> Logger -> fail $"{context}: only loggers have a logCall."
        | Some "delegate" -> LogCall.Delegate
        | Some "attribute" -> LogCall.Attribute
        | Some other -> fail $"{context}: unknown logCall \"{other}\"."

    { Id = id
      Assembly = defaultArg (str e "assembly") ""
      Type = required e "type" context
      Members = members
      IncludeDerivedTypes =
        match e.TryGetProperty "includeDerivedTypes" with
        | true, v -> v.ValueKind = JsonValueKind.True
        | _ -> false
      Kind = k
      Category = category
      Payload =
        match e.TryGetProperty "payload" with
        | true, v when v.ValueKind = JsonValueKind.Array ->
            [ for p in v.EnumerateArray() ->
                  let property = str p "property"

                  let argument =
                      match p.TryGetProperty "argument" with
                      | true, a -> a.GetInt32()
                      | _ when property.IsSome -> -1
                      | _ -> fail $"{context}: a payload needs an argument or a property."

                  { Argument = argument
                    Role = p.GetProperty("role").GetString()
                    Property = property
                    ConstructorArgument =
                      match p.TryGetProperty "constructorArgument" with
                      | true, a -> Some(a.GetInt32())
                      | _ -> None } ]
        | _ -> []
      HttpMethod = str e "httpMethod"
      RequestPart = str e "requestPart" |> Option.map (fun p -> requestPart p context)
      WhenFirstParameter = str e "whenFirstParameter"
      BlindSpot = str e "blindSpot"
      ComparesTo = str e "comparesTo"
      Dispatch = dispatch
      LogCall = logCall
      Configuration = configuration
      Source = source }

/// A catalog document as written, before `extends` is resolved.
type CatalogDocument =
    { Name: string
      Extends: string option
      Add: Rule list
      Remove: string list }

let parse (fallbackName: string) (json: string) : CatalogDocument =
    use doc =
        try
            JsonDocument.Parse(json, JsonDocumentOptions(CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true))
        with :? JsonException as e ->
            fail $"{fallbackName}: invalid JSON: {e.Message}"

    let root = doc.RootElement
    let name = defaultArg (str root "name") fallbackName

    let rules (property: string) =
        match root.TryGetProperty property with
        | true, v when v.ValueKind = JsonValueKind.Array -> [ for r in v.EnumerateArray() -> parseRule name r ]
        | _ -> []

    let all = rules "rules" @ rules "add"

    match all |> List.countBy (fun r -> r.Id) |> List.tryFind (fun (_, n) -> n > 1) with
    | Some(id, _) -> fail $"{name}: rule id \"{id}\" is defined twice."
    | None ->
        { Name = name
          Extends = str root "extends"
          Add = all
          Remove = strings root "remove" }

let private embedded (name: string) =
    use stream = Assembly.GetExecutingAssembly().GetManifestResourceStream $"catalogs/{name}.json"

    if isNull stream then
        None
    else
        use reader = new StreamReader(stream)
        Some(reader.ReadToEnd())

/// Names of the catalogs that ship with the tool, oldest first.
let builtInNames =
    Assembly.GetExecutingAssembly().GetManifestResourceNames()
    |> Array.filter (fun n -> n.StartsWith "catalogs/" && n.EndsWith ".json")
    |> Array.map (fun n -> n.Substring(9, n.Length - 14))
    |> Array.toList

/// The .NET version a catalog or target framework names: "net8.0" -> 8.0, "net10.0-windows" -> 10.0.
let tryVersion (tfm: string) =
    let t = tfm.ToLowerInvariant()

    if t.StartsWith "net" && t.Length > 3 && Char.IsDigit t[3] then
        let core = t.Substring(3).Split('-').[0]

        match Version.TryParse(if core.Contains '.' then core else core + ".0") with
        | true, v when v.Major >= 5 -> Some v
        | _ -> None
    else
        None

/// Resolves a document against its chain of `extends`, using `lookup` for earlier catalogs.
let rec resolve (lookup: string -> CatalogDocument option) (doc: CatalogDocument) (depth: int) : Catalog =
    if depth > 16 then
        fail $"{doc.Name}: extends chain too deep."

    let baseRules =
        match doc.Extends with
        | None -> []
        | Some parent ->
            match lookup parent with
            | Some p -> (resolve lookup p (depth + 1)).Rules
            | None -> fail $"{doc.Name}: extends unknown catalog \"{parent}\"."

    let removed = set doc.Remove
    let added = doc.Add |> List.map (fun r -> r.Id) |> set

    for r in doc.Remove do
        if not (baseRules |> List.exists (fun b -> b.Id = r)) then
            fail $"{doc.Name}: removes unknown rule \"{r}\"."

    { Name = doc.Name
      Rules = (baseRules |> List.filter (fun r -> not (removed.Contains r.Id) && not (added.Contains r.Id))) @ doc.Add }

let private builtInDocument (name: string) =
    embedded name |> Option.map (parse name)

/// A catalog file resolved against the built-in catalogs it extends, e.g. a draft for the next .NET version.
let resolveFile (path: string) : Catalog =
    if not (File.Exists path) then
        fail $"Catalog file not found: {path}"

    resolve builtInDocument (parse (Path.GetFileNameWithoutExtension path) (File.ReadAllText path)) 0

let loadBuiltIn (name: string) : Catalog =
    match builtInDocument name with
    | Some doc -> resolve builtInDocument doc 0
    | None -> fail $"No built-in catalog named \"{name}\"."

/// The built-in catalog for a target framework: the newest catalog not newer than it. A target newer than
/// every catalog gets the newest one, flagged as a fallback (plan section 8.2). Targets that name no .NET
/// version (netstandard2.0) get the oldest catalog.
let forTargetFramework (tfm: string) : CatalogChoice =
    let versions =
        builtInNames
        |> List.choose (fun n -> tryVersion n |> Option.map (fun v -> v, n))
        |> List.sortBy fst

    if versions.IsEmpty then
        fail "No built-in catalogs."

    match tryVersion tfm with
    | None ->
        { Catalog = loadBuiltIn (snd versions.Head)
          Fallback = false }
    | Some wanted ->
        let newest = List.last versions

        match versions |> List.filter (fun (v, _) -> v <= wanted) |> List.tryLast with
        | Some(v, name) ->
            { Catalog = loadBuiltIn name
              Fallback = wanted.Major > (fst newest).Major && v = fst newest }
        | None ->
            { Catalog = loadBuiltIn (snd versions.Head)
              Fallback = false }

/// A user catalog file. It may extend a built-in catalog; otherwise its rules are added to whichever
/// catalog each project uses.
let loadFile (path: string) : CatalogDocument =
    if not (File.Exists path) then
        fail $"Catalog file not found: {path}"

    parse (Path.GetFileNameWithoutExtension path) (File.ReadAllText path)

/// Adds user rules to a catalog. A user rule with the id of an existing rule replaces it; `remove` drops rules.
let extend (catalog: Catalog) (extra: CatalogDocument list) : Catalog =
    extra
    |> List.fold
        (fun (c: Catalog) doc ->
            let replaced = doc.Add |> List.map (fun r -> r.Id) |> set
            let removed = set doc.Remove

            { Name = c.Name + "+" + doc.Name
              Rules = (c.Rules |> List.filter (fun r -> not (replaced.Contains r.Id) && not (removed.Contains r.Id))) @ doc.Add })
        catalog
