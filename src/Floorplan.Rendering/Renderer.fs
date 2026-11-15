/// Renderers receive the analysis model and nothing else (plan section 7.1).
namespace Floorplan.Rendering

open Floorplan.Model

type OutputTarget =
    { /// Directory for multi-file output, or the file for single-file output.
      Path: string
      SingleFile: bool }

type RenderError = RenderError of string

type IRenderer =
    abstract Name: string
    abstract Render: model: AnalysisModel * output: OutputTarget -> Result<unit, RenderError>
