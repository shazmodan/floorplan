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

let private inline' (html: string) (css: string) (js: string) (data: string) =
    let script (body: string) = "<script>\n" + body.Replace("</script", "<\\/script") + "\n</script>"

    html
        .Replace("<link rel=\"stylesheet\" href=\"app.css\">", "<style>\n" + css + "\n</style>")
        .Replace("<script src=\"data.js\"></script>", script data)
        .Replace("<script src=\"app.js\"></script>", script js)

let render (model: AnalysisModel) (output: OutputTarget) =
    let html = asset "index.html"
    let css = asset "app.css"
    let js = asset "app.js"
    let data = dataScript model

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
        // The layout first: Structurizr merges a workspace.json older than the DSL into it, and takes a newer one
        // as the whole workspace.
        File.WriteAllText(Path.Combine(output.Path, StructurizrDsl.LayoutFileName), StructurizrDsl.writeLayout model)
        File.WriteAllText(Path.Combine(output.Path, StructurizrDsl.FileName), StructurizrDsl.write model)
        Path.Combine(output.Path, "index.html")

let create () =
    { new IRenderer with
        member _.Name = "site"

        member _.Render(model, output) =
            try
                render model output |> ignore
                Ok()
            with e ->
                Error(RenderError e.Message) }
