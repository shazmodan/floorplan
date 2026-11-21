/// What a long run is doing, for the people watching it: each stage reports its steps here, and the front end (the
/// CLI) shows them. Nothing is shown unless a front end listens.
module Floorplan.Model.Progress

let mutable private sink: string -> unit = ignore

/// Sends every report to `listener` from now on; reports may come from several threads at once.
let listen (listener: string -> unit) = sink <- listener

/// Stops showing reports.
let stop () = sink <- ignore

/// Reports a step that is starting.
let report (message: string) = sink message
