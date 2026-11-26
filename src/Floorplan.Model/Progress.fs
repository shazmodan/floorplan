/// What a long run is doing, for the people watching it: each stage reports its steps here, and the front end (the
/// CLI) shows them. Nothing is shown unless a front end listens.
module Floorplan.Model.Progress

let mutable private sink: string -> unit = ignore

/// What the reports of the work running here are about (a solution of several analyzed at once), for each thread
/// and task that work starts.
let private about = System.Threading.AsyncLocal<string>()

/// Sends every report to `listener` from now on; reports may come from several threads at once.
let listen (listener: string -> unit) = sink <- listener

/// Stops showing reports.
let stop () = sink <- ignore

/// Reports a step that is starting.
let report (message: string) =
    match about.Value with
    | null
    | "" -> sink message
    | prefix -> sink $"{prefix}: {message}"

/// Runs `work` with its reports, and those of the threads and tasks it starts, after `prefix`.
let within (prefix: string) (work: unit -> 'T) : 'T =
    let outer = about.Value
    about.Value <- prefix

    try
        work ()
    finally
        about.Value <- outer
