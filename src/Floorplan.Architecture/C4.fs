/// The C4 model of an analyzed solution (docs/c4-plan.md): its four levels, derived from the analysis model's
/// projects, units, dependencies, entry points and exit points. Nothing here knows a language or a library.
module Floorplan.Architecture.C4

open Floorplan.Model

/// The C4 model of the solution the analysis model describes.
let derive (model: AnalysisModel) : C4Model = AnalysisModel.emptyArchitecture
