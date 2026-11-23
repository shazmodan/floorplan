/// Golden files (plan section 9): results are checked in as text, so any change shows up as a diff in review.
/// Set FLOORPLAN_UPDATE_GOLDEN=1 to rewrite them after an intended change.
module Floorplan.Tests.Golden

open System
open System.IO
open Expecto

let goldenDir = Path.Combine(ArchitectureTests.repoRoot, "tests", "golden")

let check (name: string) (actual: string) =
    let file = Path.Combine(goldenDir, name)
    let actual = actual.Replace("\r\n", "\n")

    if Environment.GetEnvironmentVariable "FLOORPLAN_UPDATE_GOLDEN" = "1" || not (File.Exists file) then
        Directory.CreateDirectory goldenDir |> ignore
        File.WriteAllText(file, actual)
    else
        let expected = File.ReadAllText(file).Replace("\r\n", "\n")

        if expected <> actual then
            File.WriteAllText(file + ".actual", actual)
            failtestf "%s differs from the golden file. Compare with %s.actual, or set FLOORPLAN_UPDATE_GOLDEN=1." name file

/// A path relative to the fixtures folder, with forward slashes, for machine-independent output. A file in the
/// NuGet package cache (a package's source, named in its symbols) is given relative to the cache, which is
/// elsewhere on every machine: `<packages>/corewcf.primitives/...`.
let relative (path: string) =
    let normal = path.Replace('\\', '/')

    let cache =
        match Environment.GetEnvironmentVariable "NUGET_PACKAGES" with
        | null
        | "" -> None
        | root -> Some(root.Replace('\\', '/').TrimEnd('/') + "/")

    match cache with
    | Some root when normal.StartsWith(root, StringComparison.OrdinalIgnoreCase) -> "<packages>/" + normal.Substring root.Length
    | _ ->
        match normal.IndexOf("/.nuget/packages/", StringComparison.OrdinalIgnoreCase) with
        | i when i >= 0 -> "<packages>/" + normal.Substring(i + "/.nuget/packages/".Length)
        | _ -> Path.GetRelativePath(Fixtures.fixturesDir, path).Replace('\\', '/')
