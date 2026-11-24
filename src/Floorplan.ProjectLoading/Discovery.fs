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

/// The solutions in a folder of solutions (docs/landscape-plan.md, "Finding the solutions"): each one a system.
type FoundSolutions =
    { /// The solutions found, in path order: the first in each branch of the folder tree.
      Solutions: string list
      /// Solutions in the folder of another one (a samples/ or tools/ solution): part of that system, analyzed only
      /// when named.
      Nested: string list }

let private skippedFolders =
    set [ "bin"; "obj"; "node_modules"; ".git"; ".vs"; "artifacts" ]

/// The solution files directly in a folder; of X.sln and X.slnx, the .slnx.
let private solutionsIn (dir: string) =
    let files = Directory.GetFiles dir |> Array.filter isSolution |> Array.sort |> Array.toList
    let stem (f: string) = Path.Combine(Path.GetDirectoryName f, Path.GetFileNameWithoutExtension f).ToLowerInvariant()
    let newer = files |> List.filter (fun f -> extensionOf f = ".slnx") |> List.map stem |> Set.ofList
    files |> List.filter (fun f -> extensionOf f = ".slnx" || not (newer.Contains(stem f)))

/// Whether a path is a folder with no solution or project of its own, which `resolve` gives no meaning: a folder
/// of solutions.
let isFolderOfSolutions (input: string option) =
    let path = Path.GetFullPath(defaultArg input Environment.CurrentDirectory)

    Directory.Exists path
    && Directory.GetFiles path |> Array.forall (fun f -> not (isSolution f || isProject f))

/// The solutions in the subfolders of a folder. Build output, package and tool folders and hidden folders are
/// skipped, and the search stops at a folder with a solution: solutions further down belong to its system.
let findSolutions (folder: string) : FoundSolutions =
    let root = Path.GetFullPath folder

    let hidden (dir: string) =
        let name = Path.GetFileName dir

        name.StartsWith "."
        || skippedFolders.Contains(name.ToLowerInvariant())
        || (try File.GetAttributes(dir).HasFlag FileAttributes.Hidden with _ -> false)

    let rec walk (dir: string) (inside: bool) : (string * bool) list =
        let here = if dir = root then [] else solutionsIn dir
        let below = inside || not here.IsEmpty

        let children =
            try Directory.GetDirectories dir |> Array.sort |> Array.toList with _ -> []

        (here |> List.map (fun s -> s, inside))
        @ (children |> List.filter (hidden >> not) |> List.collect (fun c -> walk c below))

    let all = walk root false

    { Solutions = all |> List.filter (snd >> not) |> List.map fst
      Nested = all |> List.filter snd |> List.map fst }
