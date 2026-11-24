module Floorplan.Cli.Program

open System
open Argu
open Floorplan.Model
open Floorplan.Cli.Options

/// A folder of solutions (docs/landscape-plan.md): each one analyzed, then the landscape.
let private analyzeLandscape (options: AnalyzeOptions) (found: Floorplan.ProjectLoading.Discovery.FoundSolutions) =
    let result =
        use display = new StatusLine.Display(options.Quiet)
        Progress.listen display.Report

        try
            Pipeline.runLandscape display.Report options found
        finally
            Progress.stop ()

    match result with
    | Error message ->
        if not options.Silent then
            eprintfn "error: %s" message

        1
    | Ok outcome ->
        let l = outcome.Landscape

        if not options.Quiet then
            for s in l.Solutions do
                match s.Failure with
                | Some failure -> eprintfn "%s: not analyzed. %s" s.Name failure
                | None ->
                    eprintfn
                        "%s: %d projects, %d entry points, %d exit points%s"
                        s.Name
                        s.Projects
                        s.EntryPoints
                        s.ExitPoints
                        (if s.Errors > 0 then $", {s.Errors} errors" else "")

            if not l.Nested.IsEmpty then
                eprintfn "Not analyzed, in another solution's folder (name them with --solutions): %s" (String.Join(", ", l.Nested))

            eprintfn "%d solutions, %d interactions between them" l.Solutions.Length l.Interactions.Length

        if not options.Silent then
            printfn "%s" outcome.Written

        if outcome.HadFailures then 1 else 0

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
        if not options.Silent then
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

        if not options.Silent then
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
                | Ok options ->
                    match Pipeline.folderOfSolutions options with
                    | Some found -> analyzeLandscape options found
                    | None -> analyze options
                | Error message -> usageError message
            | None -> usageError (parser.PrintUsage())
    with :? ArguParseException as e ->
        usageError e.Message
