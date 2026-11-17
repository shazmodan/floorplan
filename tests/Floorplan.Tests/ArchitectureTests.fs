/// Rules from plan sections 3.5 and 9 that keep the design open for C# and free of framework knowledge.
module Floorplan.Tests.ArchitectureTests

open System
open System.IO
open System.Text.RegularExpressions
open Expecto

let repoRoot =
    let rec up (dir: DirectoryInfo) =
        if isNull dir then failwith "Repository root not found."
        elif File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")) then dir.FullName
        else up dir.Parent

    up (DirectoryInfo AppContext.BaseDirectory)

let private srcDir = Path.Combine(repoRoot, "src")

let private projectFile name =
    Path.Combine(srcDir, name, name + ".fsproj")

let private references (fsproj: string) =
    let text = File.ReadAllText fsproj

    let projects =
        Regex.Matches(text, "ProjectReference Include=\"([^\"]+)\"")
        // Project files use backslashes, which are not separators on Linux and macOS.
        |> Seq.map (fun m ->
            let relative = m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar)
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName fsproj, relative)))
        |> Seq.toList

    let packages =
        Regex.Matches(text, "PackageReference Include=\"([^\"]+)\"")
        |> Seq.map (fun m -> m.Groups[1].Value)
        |> Seq.toList

    projects, packages

/// Every project and package a project depends on, transitively through project references.
let rec private closure (fsproj: string) =
    let projects, packages = references fsproj

    [ for p in projects do
          yield Path.GetFileNameWithoutExtension p
          yield! closure p
      yield! packages ]

/// Projects that must stay language-neutral (plan section 3.5).
let neutralProjects =
    [ "Floorplan.Analysis"; "Floorplan.Layout"; "Floorplan.Rendering" ]

/// Framework names that must not appear in src/ (plan section 9). Frameworks exist only as test fixtures.
let frameworkNames =
    [ "Giraffe"; "Falco"; "Saturn"; "Suave"; "Oxpecker"; "Fable.Remoting"; "WebSharper"; "Freya" ]

[<Tests>]
let tests =
    testList
        "Architecture"
        [ for name in neutralProjects do
              test $"{name} does not depend on a compiler (FCS, Roslyn) or any *.FSharp or *.CSharp project" {
                  let file = projectFile name

                  if File.Exists file then
                      let offending =
                          closure file
                          |> List.filter (fun d ->
                              d = "FSharp.Compiler.Service"
                              || d.StartsWith "Microsoft.CodeAnalysis"
                              || d.EndsWith ".FSharp"
                              || d.EndsWith ".CSharp")

                      Expect.isEmpty offending $"{name} must stay language-neutral"
              }

          test "no framework name appears in src/" {
              let hits =
                  Directory.EnumerateFiles(srcDir, "*.*", SearchOption.AllDirectories)
                  |> Seq.filter (fun f ->
                      let sep = string Path.DirectorySeparatorChar
                      not (f.Contains(sep + "bin" + sep) || f.Contains(sep + "obj" + sep)))
                  |> Seq.collect (fun f ->
                      let text = File.ReadAllText f

                      frameworkNames
                      |> Seq.filter (fun n -> Regex.IsMatch(text, $@"\b{Regex.Escape n}\b"))
                      |> Seq.map (fun n -> $"{Path.GetRelativePath(repoRoot, f)}: {n}"))
                  |> Seq.toList

              Expect.isEmpty hits "framework names belong in tests/fixtures only"
          } ]
