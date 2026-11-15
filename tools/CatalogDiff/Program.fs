/// CatalogDiff (plan section 8.3, step 2): lists public API added or removed between two .NET versions in the
/// watched namespaces, marking what the catalog already covers, so the yearly delta catalog can be reviewed.
///
///     dotnet run --project tools/CatalogDiff -- --old 10.0.15 --new 11.0.0 [--catalog net10.0] [--out diff.md]
///     dotnet run --project tools/CatalogDiff -- --old-dir <ref folder> --new-dir <ref folder> ...
///     ... --catalog-file src/Floorplan.Catalog/drafts/net11.0.json   (check a draft delta)
module CatalogDiff.Program

open System
open System.IO
open System.Text
open Floorplan.Catalog

type private Args =
    { Old: string list
      New: string list
      OldName: string
      NewName: string
      Catalog: string
      CatalogFile: string option
      Out: string option }

let private parse (argv: string[]) =
    let cache = Path.Combine(Path.GetTempPath(), "floorplan-catalogdiff")
    let value (name: string) = argv |> Array.tryFindIndex ((=) name) |> Option.map (fun i -> argv[i + 1])
    let values (name: string) = argv |> Array.indexed |> Array.filter (fun (_, a) -> a = name) |> Array.map (fun (i, _) -> argv[i + 1]) |> Array.toList

    let side (version: string) (dirs: string) =
        match value version, values dirs with
        | Some v, _ -> Packs.resolve cache v, Packs.tfmOf v
        | None, (_ :: _ as ds) -> ds, String.Join(", ", ds |> List.map Path.GetFileName)
        | None, [] -> failwith $"Give {version} <pack version> or {dirs} <folder>."

    let oldDirs, oldName = side "--old" "--old-dir"
    let newDirs, newName = side "--new" "--new-dir"

    { Old = oldDirs
      New = newDirs
      OldName = oldName
      NewName = newName
      Catalog = defaultArg (value "--catalog") (CatalogLoader.builtInNames |> List.last)
      CatalogFile = value "--catalog-file"
      Out = value "--out" }

let private report (args: Args) =
    let before = ApiSurface.read args.Old
    let after = ApiSurface.read args.New
    let catalog =
        match args.CatalogFile with
        | Some file -> CatalogLoader.resolveFile file
        | None -> CatalogLoader.loadBuiltIn args.Catalog
    let matcher = Matcher(catalog, ApiSurface.supertypes after)

    let added = after.Members |> Map.filter (fun k _ -> not (before.Members.ContainsKey k)) |> Map.toList |> List.map snd
    let removed = before.Members |> Map.filter (fun k _ -> not (after.Members.ContainsKey k)) |> Map.toList |> List.map snd

    let b = StringBuilder()
    let line (s: string) = b.AppendLine s |> ignore

    line $"# Public API changes, {args.OldName} -> {args.NewName}"
    line ""
    line $"""Watched namespaces: {String.Join(", ", ApiSurface.watched)}. Catalog checked: {catalog.Name} ({catalog.Rules.Length} rules)."""
    line ""
    line $"{added.Length} members added, {removed.Length} removed. A member marked with a rule is already covered;"
    line "the others need review: add a rule to the delta catalog when the member is a side-effect, a request"
    line "source or a handler registration."
    line ""

    let section (title: string) (members: ApiSurface.Member list) =
        line $"## {title} ({members.Length})"
        line ""

        for (typeName, ms) in members |> List.groupBy (fun m -> m.Type) |> List.sortBy fst do
            line $"### {typeName}"
            line ""

            for m in ms |> List.sortBy (fun m -> m.Signature) do
                let covered =
                    match matcher.Match m.Method with
                    | [] -> ""
                    | rules -> "  — covered by " + String.Join(", ", rules |> List.map (fun r -> r.Id))

                let name = m.Signature.Substring(m.Signature.IndexOf "::" + 2)
                line $"- `{name}`{covered}"

            line ""

    section "Added" added
    section "Removed" removed
    b.ToString()

[<EntryPoint>]
let main argv =
    try
        let args = parse argv
        let text = report args

        match args.Out with
        | Some file ->
            File.WriteAllText(file, text)
            printfn "%s" file
        | None -> printfn "%s" text

        0
    with e ->
        eprintfn "error: %s" e.Message
        1
