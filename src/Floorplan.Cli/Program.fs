module Floorplan.Cli.Program

open System
open Argu
open Floorplan.Model
open Floorplan.Cli.Options

/// Exit codes: 0 success, 1 analysis failed or some projects could not be analyzed, 2 usage error.
let private analyze (options: AnalyzeOptions) =
    let log message =
        if not options.Quiet then
            eprintfn "%s" message

    match Pipeline.run log options with
    | Error message ->
        eprintfn "error: %s" message
        1
    | Ok outcome ->
        let m = outcome.Model

        if not options.Quiet then
            let units = m.Units |> List.filter (fun u -> u.Kind <> UnitKind.ProjectUnit) |> List.length
            let exits =
                m.ExitPoints |> List.filter (fun x -> not (EffectCategory.hiddenByDefault |> List.contains x.Category))

            eprintfn
                "%d projects, %d units, %d dependencies, %d entry points, %d exit points"
                m.Projects.Length
                units
                m.Dependencies.Length
                m.EntryPoints.Length
                exits.Length

            for d in m.Diagnostics |> List.filter (fun d -> d.Severity <> DiagnosticSeverity.Info) do
                eprintfn "%s: %s" (Json.unionName d.Severity) d.Message

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
