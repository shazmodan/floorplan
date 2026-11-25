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

/// What one file may hold of a landscape: past it, the landscape is written as a folder. A browser reads the whole
/// file however much of it a page shows; a solution's model is a few megabytes, so this is some twenty of them.
let singleFileLimit = 64L * 1024L * 1024L

/// JSON inside a `<script type="application/json">` element: "<" written as an escape, so nothing in it can end
/// the element or start a comment.
let private jsonBlock (id: string) (json: string) =
    $"<script type=\"application/json\" id=\"{id}\">" + json.Replace("<", "\u003c") + "</script>\n"

/// The landscape in one HTML file (`--single-file`): its pages, and every solution's site, which the file opens as
/// `<file>?solution=<slug>`. Each model is kept as JSON and only the one shown is read. Refused, with the size, when
/// the file would be larger than `limit`.
let renderLandscapeFile (landscape: Landscape) (models: (string * AnalysisModel) list) (path: string) (limit: int64) : Result<string, string> =
    let file =
        if Path.GetExtension(path).ToLowerInvariant() = ".html" then path
        else Path.Combine(path, "floorplan.html")

    let solutions =
        landscape.Solutions |> List.map (fun s -> {| name = s.Name; slug = s.Slug |})

    let link =
        Json.serializeValue false {| landscape = landscape.Name; solutions = solutions |}

    let blocks =
        jsonBlock "floorplan-landscape" (Json.serializeValue false landscape)
        + (models |> List.map (fun (slug, model) -> jsonBlock $"floorplan-solution:{slug}" (Json.serialize false model)) |> String.concat "")

    // Which page the file is: the solution its query names, or the landscape.
    let choose =
        "(function () {\n"
        + "  var m = /[?&]solution=([^&#]*)/.exec(location.search), slug = m ? decodeURIComponent(m[1]) : null;\n"
        + "  function read(id) { var e = document.getElementById(id); return e ? JSON.parse(e.textContent) : null; }\n"
        + "  var model = slug ? read(\"floorplan-solution:\" + slug) : null;\n"
        + "  window.FLOORPLAN_SINGLE_FILE = true;\n"
        + "  if (model) {\n"
        + "    var link = " + link.Replace("<", "\u003c") + ";\n"
        + "    link.current = slug;\n"
        + "    window.FLOORPLAN_DATA = model;\n"
        + "    window.FLOORPLAN_LANDSCAPE_LINK = link;\n"
        + "  } else window.FLOORPLAN_LANDSCAPE = read(\"floorplan-landscape\");\n"
        + "})();\n"

    // The models go before the script that reads them; with "<" escaped, none can match what inline' replaces.
    let page = (asset "index.html").Replace("<script src=\"data.js\"></script>", blocks + "<script src=\"data.js\"></script>")
    let html = inline' page (asset "app.css") (asset "app.js") choose

    let size = int64 (System.Text.Encoding.UTF8.GetByteCount html)

    if size > limit then
        let mb (n: int64) = float n / (1024.0 * 1024.0)
        Error(sprintf "The landscape would be %.0f MB in one file, more than the %.0f MB one file is allowed; write it as a folder (without --single-file)." (mb size) (mb limit))
    else
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath file)) |> ignore
        File.WriteAllText(file, html)
        Ok file

let create () =
    { new IRenderer with
        member _.Name = "site"

        member _.Render(model, output) =
            try
                render model output |> ignore
                Ok()
            with e ->
                Error(RenderError e.Message) }
