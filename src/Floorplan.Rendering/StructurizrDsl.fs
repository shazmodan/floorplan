/// The C4 model as a Structurizr DSL workspace (https://docs.structurizr.com/dsl), so it opens in Structurizr Lite
/// and goes through Structurizr's own exporters (PlantUML, Mermaid, ...).
///
/// People map to `person`, the system and external systems to `softwareSystem`, applications to `container`,
/// units to `component`. The DSL has no code level: a relationship of a code element is written from or to its
/// component, and code views are left out. Relationships are written between the most specific elements;
/// Structurizr implies the ones between the elements that contain them.
module Floorplan.Rendering.StructurizrDsl

open System
open System.Collections.Generic
open System.Text
open Floorplan.Model

/// The file written next to the website.
[<Literal>]
let FileName = "workspace.dsl"

/// A DSL string: the DSL has no escapes for quotes or line breaks.
let private quoted (s: string) =
    let s = s.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'")
    "\"" + s + "\""

/// A DSL-safe identifier from a Floorplan id (which holds ':', '|' and '.'): letters, digits and '_'.
let private safe (id: string) =
    let chars = id |> Seq.map (fun c -> if Char.IsAsciiLetterOrDigit c then c else '_') |> Array.ofSeq
    let s = String(chars)
    if s = "" || Char.IsAsciiDigit s[0] then "_" + s else s

/// Identifiers for every element, unique after making them safe.
let private identifiers (elements: C4Element list) =
    let taken = HashSet<string>()

    elements
    |> List.map (fun e ->
        let basis = safe e.Id
        let mutable candidate = basis
        let mutable n = 2

        while not (taken.Add candidate) do
            candidate <- $"{basis}_{n}"
            n <- n + 1

        e.Id, candidate)
    |> Map.ofList

/// Names unique among an element's siblings, as Structurizr requires: a library's unit named like another
/// library's (`Util`) is qualified by its project, anything else by its id.
let private names (elements: C4Element list) =
    let qualifier (e: C4Element) =
        match e.Kind with
        | C4Kind.Component ->
            // component:<container project>|<project>:<unit>
            let unit = e.Id.Substring(e.Id.IndexOf '|' + 1)
            unit.Substring(0, max 0 (unit.IndexOf ':'))
        | _ -> e.Id

    elements
    |> List.groupBy (fun e -> e.Parent)
    |> List.collect (fun (_, siblings) ->
        siblings
        |> List.groupBy (fun e -> e.Name.ToLowerInvariant())
        |> List.collect (fun (_, same) ->
            match same with
            | [ e ] -> [ e.Id, e.Name ]
            | many -> many |> List.map (fun e -> e.Id, $"{e.Name} ({qualifier e})")))
    |> Map.ofList

let private tags (extra: string list) (e: C4Element) =
    match extra @ e.Tags with
    | [] -> None
    | all -> Some(String.Join(",", all |> List.map (fun t -> t.Replace(",", ";"))))

/// The workspace for a model.
let write (model: AnalysisModel) : string =
    let c4 = model.Architecture
    let shown = c4.Elements |> List.filter (fun e -> e.Kind <> C4Kind.Code)
    let byId = c4.Elements |> List.map (fun e -> e.Id, e) |> Map.ofList
    let ident = identifiers shown
    let name = names shown

    let children (parent: string) =
        shown |> List.filter (fun e -> e.Parent = Some parent)

    let sb = StringBuilder()
    let line (indent: int) (text: string) = sb.Append(String(' ', 4 * indent)).Append(text).Append('\n') |> ignore

    // `<id> = <keyword> "name" ["description" ["technology"]] ["tags"]`, each argument kept when one after it is.
    let declaration (e: C4Element) (keyword: string) (args: string option list) =
        let args = args |> List.rev |> List.skipWhile Option.isNone |> List.rev
        let text = args |> List.map (fun a -> quoted (defaultArg a "")) |> String.concat " "
        $"{ident[e.Id]} = {keyword} {quoted name[e.Id]}" + (if text = "" then "" else " " + text)

    let description (e: C4Element) = if e.Description = "" then None else Some e.Description

    let rec declare (indent: int) (e: C4Element) =
        let decl =
            match e.Kind with
            | C4Kind.Person -> declaration e "person" [ description e; tags [] e ]
            | C4Kind.ExternalSystem -> declaration e "softwareSystem" [ description e; tags [ "External" ] e ]
            | C4Kind.SoftwareSystem -> declaration e "softwareSystem" [ description e; tags [] e ]
            | C4Kind.Container -> declaration e "container" [ description e; e.Technology; tags [] e ]
            | C4Kind.Component
            | C4Kind.Code -> declaration e "component" [ description e; e.Technology; tags [] e ]

        match children e.Id with
        | [] -> line indent decl
        | parts ->
            line indent (decl + " {")

            for p in parts do
                declare (indent + 1) p

            line indent "}"

    // The element a relationship end is written at: a code element's component.
    let rec at (id: string) =
        match byId.TryFind id with
        | Some e when e.Kind = C4Kind.Code -> e.Parent |> Option.map at |> Option.defaultValue id
        | _ -> id

    let rec ancestors (id: string) =
        match byId.TryFind id |> Option.bind (fun e -> e.Parent) with
        | Some p -> p :: ancestors p
        | None -> []

    let relationships =
        c4.Relationships
        |> List.map (fun r -> at r.From, at r.To, r)
        |> List.filter (fun (a, b, _) ->
            // Structurizr refuses a relationship between an element and one it contains.
            a <> b
            && ident.ContainsKey a
            && ident.ContainsKey b
            && not (ancestors a |> List.contains b)
            && not (ancestors b |> List.contains a))
        |> List.groupBy (fun (a, b, r) -> a, b, r.Description)
        |> List.map (fun ((a, b, d), rs) ->
            let technology =
                rs
                |> List.choose (fun (_, _, r) -> r.Technology)
                |> List.collect (fun t -> t.Split(", ") |> List.ofArray)
                |> List.distinct
                |> List.sort

            a, b, d, (if technology.IsEmpty then None else Some(String.Join(", ", technology))))
        |> List.sort

    let system = c4.Elements |> List.tryFind (fun e -> e.Kind = C4Kind.SoftwareSystem)
    let title = system |> Option.map (fun s -> s.Name) |> Option.defaultValue model.Solution.Name

    let about = quoted (title + ", derived by Floorplan from its code")
    line 0 $"workspace {quoted title} {about} {{"
    line 1 "model {"
    // Lift each relationship with its own description ("reads files" and "writes files" both), as Floorplan's
    // views do; Structurizr's default lifts only the first between two elements.
    line 2 "!impliedRelationships \"com.structurizr.model.CreateImpliedRelationshipsUnlessSameRelationshipExistsStrategy\""

    for e in shown |> List.filter (fun e -> e.Parent.IsNone) do
        declare 2 e

    if not relationships.IsEmpty then
        line 0 ""

    for a, b, d, technology in relationships do
        let tech = technology |> Option.map (fun t -> " " + quoted t) |> Option.defaultValue ""
        line 2 $"{ident[a]} -> {ident[b]} {quoted d}{tech}"

    line 1 "}"
    line 0 ""
    line 1 "views {"

    for v in c4.Views do
        let keyword =
            match v.Level with
            | C4Level.Context -> Some "systemContext"
            | C4Level.Containers -> Some "container"
            | C4Level.Components -> Some "component"
            | C4Level.Code -> None

        match keyword with
        | Some keyword when ident.ContainsKey v.Scope ->
            let included = v.Elements |> List.filter ident.ContainsKey |> List.map (fun id -> ident[id])
            line 2 $"{keyword} {ident[v.Scope]} {quoted (safe v.Id)} {{"
            line 3 ("include " + String.Join(" ", included))
            line 3 "autoLayout tb"
            line 2 "}"
        | _ -> ()

    line 0 ""
    line 2 "styles {"

    for tag, style in
        [ "Person", [ "shape Person"; "background #08427b"; "color #ffffff" ]
          "Software System", [ "background #1168bd"; "color #ffffff" ]
          "Container", [ "background #438dd5"; "color #ffffff" ]
          "Component", [ "background #85bbf0"; "color #000000" ]
          "External", [ "background #999999"; "color #ffffff" ] ] do
        line 3 $"element {quoted tag} {{"

        for s in style do
            line 4 s

        line 3 "}"

    line 2 "}"
    line 1 "}"
    line 0 "}"
    sb.ToString()
