/// Runs the .NET SDK. The analysis reads built assemblies, so the solution must build (plan section 3.1).
module Floorplan.ProjectLoading.Build

open System.Diagnostics

type CommandResult = { ExitCode: int; Output: string }

let dotnet (arguments: string list) : CommandResult =
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
    proc.WaitForExit()

    { ExitCode = proc.ExitCode
      Output = output.Result + errors.Result }

let build (path: string) (configuration: string) =
    dotnet [ "build"; path; "-c"; configuration; "-v"; "q"; "-nologo"; "-clp:NoSummary;ErrorsOnly" ]

let restore (path: string) =
    dotnet [ "restore"; path; "-v"; "q"; "-nologo" ]
