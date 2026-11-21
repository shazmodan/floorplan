module Floorplan.Cli.Program

open System
open Argu
open Floorplan.Model
open Floorplan.Cli.Options

/// Exit codes: 0 success, 1 analysis failed or some projects could not be analyzed, 2 usage error.
let private analyze (options: AnalyzeOptions) =
    let result =
        use display = new StatusLine.Display(options.Quiet)
        Progress.listen display.Report

        try
            Pipeline.run display.Report options
        finally
            Progress.stop ()

    match result with
    | Error message ->
        eprintfn "error: %s" message
        1
    | Ok outcome ->
        let m = outcome.Model

        if not options.Quiet then
            let units = m.Units |> List.filter (fun u -> u.Kind <> UnitKind.ProjectUnit) |> List.length
            let exits =
                m.ExitPoints
                |> List.filter (fun x ->
                    not (m.Presentation.HiddenCategories |> List.contains x.Category)
                    && not (m.Presentation.HiddenConfidence |> List.contains x.Confidence))

            eprintfn
                "%d projects, %d units, %d dependencies, %d entry points, %d exit points"
                m.Projects.Length
                units
                m.Dependencies.Length
                m.EntryPoints.Length
                exits.Length

            for line in Pipeline.consoleDiagnostics 3 m.Diagnostics do
                eprintfn "%s" line

        printfn "%s" outcome.Written
        if outcome.HadFailures then 1 else 0

[<EntryPoint>]
let main argv =
    let parser =
        ArgumentParser.Create<Command>(programName = "floorplan", errorHandler = ProcessExiter())

    let usageError (message: string) =
        eprintfn "%s" message
        2

    try
        let results = parser.ParseCommandLine(argv, raiseOnUsage = true)

        if results.Contains Version then
            printfn "%s" Pipeline.toolVersion
            0
        else
            match results.TryGetResult Analyze with
            | Some args ->
                match toOptions args with
                | Ok options -> analyze options
                | Error message -> usageError message
            | None -> usageError (parser.PrintUsage())
    with :? ArguParseException as e ->
        usageError e.Message
