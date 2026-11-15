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

/// A path relative to the fixtures folder, with forward slashes, for machine-independent output.
let relative (path: string) =
    Path.GetRelativePath(Fixtures.fixturesDir, path).Replace('\\', '/')
