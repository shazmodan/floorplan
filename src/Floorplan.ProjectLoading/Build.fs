/// Runs the .NET SDK. The analysis reads built assemblies, so the solution must build (plan section 3.1).
module Floorplan.ProjectLoading.Build

open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions
open Floorplan.Model

type CommandResult = { ExitCode: int; Output: string }

/// Runs `dotnet`, calling `onLine` with each line MSBuild writes to `log` while it runs (when given).
let private run (arguments: string list) (log: (string * (string -> unit)) option) : CommandResult =
    let info =
        ProcessStartInfo("dotnet", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false)

    for a in arguments do
        info.ArgumentList.Add a

    // Keep the build output readable and independent of the user's terminal settings.
    info.Environment["DOTNET_CLI_UI_LANGUAGE"] <- "en"
    info.Environment["DOTNET_NOLOGO"] <- "1"

    use proc = Process.Start info
    let output = proc.StandardOutput.ReadToEndAsync()
    let errors = proc.StandardError.ReadToEndAsync()

    match log with
    | None -> proc.WaitForExit()
    | Some(file, onLine) ->
        // Follow the log as MSBuild writes it: new complete lines since the last read.
        let position = ref 0L
        let partial = ref ""

        let readNew () =
            try
                if File.Exists file then
                    use stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ||| FileShare.Delete)

                    if stream.Length > position.Value then
                        stream.Seek(position.Value, SeekOrigin.Begin) |> ignore
                        use reader = new StreamReader(stream)
                        let text = partial.Value + reader.ReadToEnd()
                        position.Value <- stream.Length
                        let lines = text.Split('\n')
                        partial.Value <- lines[lines.Length - 1]

                        for line in lines[.. lines.Length - 2] do
                            onLine (line.TrimEnd '\r')
            with :? IOException ->
                ()

        while not (proc.WaitForExit 200) do
            readNew ()

        proc.WaitForExit()
        readNew ()

        if partial.Value <> "" then
            onLine partial.Value

        try
            File.Delete file
        with :? IOException ->
            ()

    { ExitCode = proc.ExitCode
      Output = output.Result + errors.Result }

let dotnet (arguments: string list) : CommandResult = run arguments None

/// A project MSBuild has built, as its minimal log says: "  Name -> path".
let private built = Regex(@"^\s+(\S+) -> (.+)$", RegexOptions.Compiled)

/// Builds the solution, reporting each project as it is built. The console output keeps errors only; a log file
/// at minimal verbosity, followed while the build runs, says which projects are done.
let build (path: string) (configuration: string) =
    let log = Path.Combine(Path.GetTempPath(), $"floorplan-build-{Guid.NewGuid():N}.log")
    let count = ref 0

    let onLine (line: string) =
        let m = built.Match line

        if m.Success then
            count.Value <- count.Value + 1

            // The output folder names the target framework of a project built for several.
            let framework =
                try
                    Path.GetFileName(Path.GetDirectoryName(m.Groups[2].Value.Trim()))
                with _ ->
                    ""

            let target = if framework.StartsWith "net" then $" for {framework}" else ""
            Progress.report $"Built {m.Groups[1].Value}{target} ({count.Value} so far)"

    run
        [ "build"; path; "-c"; configuration; "-v"; "q"; "-nologo"; "-clp:NoSummary;ErrorsOnly"
          $"-flp:logfile={log};verbosity=minimal" ]
        (Some(log, onLine))

let restore (path: string) =
    dotnet [ "restore"; path; "-v"; "q"; "-nologo" ]
