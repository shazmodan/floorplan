/// The website (plan section 7.2): plain HTML, CSS and JavaScript that opens from file:// with no network. Beside
/// it, the C4 model as a Structurizr DSL workspace (not with --single-file, which writes one file).
module Floorplan.Rendering.WebsiteRenderer

open System.IO
open System.Reflection
open Floorplan.Model

let private asset (name: string) =
    let assembly = Assembly.GetExecutingAssembly()
    use stream = assembly.GetManifestResourceStream $"site/{name}"

    if isNull stream then
        failwith $"Missing embedded site asset {name}."

    use reader = new StreamReader(stream)
    reader.ReadToEnd()

/// The model as a script, because browsers block fetch() of local files. "</" is escaped so the data
/// cannot close an inline script element.
let dataScript (model: AnalysisModel) =
    let json = (Json.serialize false model).Replace("</", "<\\/")
    $"window.FLOORPLAN_DATA = {json};\n"

/// A solution's place in a landscape of solutions (docs/landscape-plan.md): its site links back to the landscape
/// and to the other solutions' sites, which are its siblings.
type LandscapeLink =
    { Landscape: string
      /// The solution's own slug, and every solution's name and slug, in the landscape's order.
      Current: string
      Solutions: (string * string) list }

let private linkScript (link: LandscapeLink) =
    let solutions =
        link.Solutions |> List.map (fun (name, slug) -> {| name = name; slug = slug |})

    let json =
        (Json.serializeValue false {| landscape = link.Landscape; current = link.Current; solutions = solutions |}).Replace("</", "<\\/")

    $"window.FLOORPLAN_LANDSCAPE_LINK = {json};\n"

let private inline' (html: string) (css: string) (js: string) (data: string) =
    let script (body: string) = "<script>\n" + body.Replace("</script", "<\\/script") + "\n</script>"

    html
        .Replace("<link rel=\"stylesheet\" href=\"app.css\">", "<style>\n" + css + "\n</style>")
        .Replace("<script src=\"data.js\"></script>", script data)
        .Replace("<script src=\"app.js\"></script>", script js)

/// The site of one solution, in a landscape when it is part of one.
let renderIn (link: LandscapeLink option) (model: AnalysisModel) (output: OutputTarget) =
    let html = asset "index.html"
    let css = asset "app.css"
    let js = asset "app.js"
    let data = dataScript model + (link |> Option.map linkScript |> Option.defaultValue "")

    if output.SingleFile then
        let file =
            if Path.GetExtension(output.Path).ToLowerInvariant() = ".html" then
                output.Path
            else
                Path.Combine(output.Path, "floorplan.html")

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath file)) |> ignore
        File.WriteAllText(file, inline' html css js data)
        file
    else
        Directory.CreateDirectory output.Path |> ignore
        File.WriteAllText(Path.Combine(output.Path, "index.html"), html)
        File.WriteAllText(Path.Combine(output.Path, "app.css"), css)
        File.WriteAllText(Path.Combine(output.Path, "app.js"), js)
        // The files beside the site, which its pages link to (not in a single file, which has none).
        let files = $"window.FLOORPLAN_FILES = [\"{StructurizrDsl.FileName}\", \"{StructurizrDsl.LayoutFileName}\"];\n"
        File.WriteAllText(Path.Combine(output.Path, "data.js"), data + files)
        // Structurizr merges a workspace.json older than the DSL into it, and takes one that is not as the whole
        // workspace. Written one after the other, both can get the same time, so the layout is dated back.
        let layout = Path.Combine(output.Path, StructurizrDsl.LayoutFileName)
        let dsl = Path.Combine(output.Path, StructurizrDsl.FileName)
        File.WriteAllText(layout, StructurizrDsl.writeLayout model)
        File.WriteAllText(dsl, StructurizrDsl.write model)
        File.SetLastWriteTimeUtc(layout, File.GetLastWriteTimeUtc(dsl).AddSeconds -2.0)
        Path.Combine(output.Path, "index.html")

let render (model: AnalysisModel) (output: OutputTarget) = renderIn None model output

/// The landscape's site: the same pages, which show a landscape when the data is one (docs/landscape-plan.md).
let renderLandscape (landscape: Landscape) (dir: string) =
    Directory.CreateDirectory dir |> ignore
    File.WriteAllText(Path.Combine(dir, "index.html"), asset "index.html")
    File.WriteAllText(Path.Combine(dir, "app.css"), asset "app.css")
    File.WriteAllText(Path.Combine(dir, "app.js"), asset "app.js")
    let json = (Json.serializeValue false landscape).Replace("</", "<\\/")
    let files = $"window.FLOORPLAN_FILES = [\"{StructurizrDsl.FileName}\", \"{StructurizrDsl.LayoutFileName}\"];\n"
    File.WriteAllText(Path.Combine(dir, "data.js"), $"window.FLOORPLAN_LANDSCAPE = {json};\n" + files)

    // The landscape as a Structurizr workspace, its probable relationships (matched by route only) dashed.
    let probableSources =
        landscape.Interactions |> List.filter (fun i -> i.Probable) |> List.collect (fun i -> i.Sources) |> Set.ofList

    let probable =
        landscape.Architecture.Relationships
        |> List.filter (fun r -> not r.Sources.IsEmpty && r.Sources |> List.forall probableSources.Contains)
        |> List.map (fun r -> r.From, r.To, r.Description)
        |> Set.ofList

    let title = landscape.Name
    let layout = Path.Combine(dir, StructurizrDsl.LayoutFileName)
    let dsl = Path.Combine(dir, StructurizrDsl.FileName)
    File.WriteAllText(layout, StructurizrDsl.writeLayoutC4 title landscape.Architecture)
    File.WriteAllText(dsl, StructurizrDsl.writeC4 title probable landscape.Architecture)
    File.SetLastWriteTimeUtc(layout, File.GetLastWriteTimeUtc(dsl).AddSeconds -2.0)
    Path.Combine(dir, "index.html")

let create () =
    { new IRenderer with
        member _.Name = "site"

        member _.Render(model, output) =
            try
                render model output |> ignore
                Ok()
            with e ->
                Error(RenderError e.Message) }
