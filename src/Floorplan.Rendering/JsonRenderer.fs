/// `--format json`: writes the analysis model itself, which makes the tool scriptable.
module Floorplan.Rendering.JsonRenderer

open System.IO
open Floorplan.Model

/// The file the model is written to: the target itself when it names a .json file, else model.json inside it.
let outputFile (output: OutputTarget) =
    if Path.GetExtension(output.Path).ToLowerInvariant() = ".json" then
        output.Path
    else
        Path.Combine(output.Path, "floorplan.json")

let create () =
    { new IRenderer with
        member _.Name = "json"

        member _.Render(model, output) =
            try
                let file = outputFile output
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath file)) |> ignore
                File.WriteAllText(file, Json.serialize true model)
                Ok()
            with e ->
                Error(RenderError e.Message) }
