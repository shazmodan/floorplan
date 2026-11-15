/// Finds what to analyze from a path the user typed, or from the current directory.
module Floorplan.ProjectLoading.Discovery

open System
open System.IO

let private solutionExtensions = [ ".slnx"; ".sln" ]
let private projectExtensions = [ ".fsproj"; ".csproj" ]

let extensionOf (path: string) = Path.GetExtension(path).ToLowerInvariant()

let isSolution path =
    solutionExtensions |> List.contains (extensionOf path)

let isProject path =
    projectExtensions |> List.contains (extensionOf path)

/// The solution or project file to analyze. A directory resolves to its single solution, or failing that
/// its single F# project.
let resolve (input: string option) : Result<string, LoadError> =
    let path = Path.GetFullPath(defaultArg input Environment.CurrentDirectory)

    if File.Exists path then
        if isSolution path || isProject path then
            Ok path
        else
            Error(UnsupportedInput $"Expected a .sln, .slnx, .fsproj or .csproj file: {path}")
    elif Directory.Exists path then
        let inDir pattern = Directory.GetFiles(path, pattern) |> Array.sort |> Array.toList

        match inDir "*.slnx" @ inDir "*.sln", inDir "*.fsproj" with
        | [ solution ], _ -> Ok solution
        | [], [ project ] -> Ok project
        | [], [] -> Error(InputNotFound $"No solution or F# project found in {path}")
        | [], projects ->
            let names = projects |> List.map Path.GetFileName |> String.concat ", "
            Error(UnsupportedInput $"Several projects in {path} ({names}); name the one to analyze.")
        | solutions, _ ->
            let names = solutions |> List.map Path.GetFileName |> String.concat ", "
            Error(UnsupportedInput $"Several solutions in {path} ({names}); name the one to analyze.")
    else
        Error(InputNotFound $"Not found: {path}")
