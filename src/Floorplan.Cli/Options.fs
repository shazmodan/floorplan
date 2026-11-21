/// Command-line arguments (plan section 7.3).
module Floorplan.Cli.Options

open System
open Argu
open Floorplan.Model

type OutputFormat =
    | Site
    | Json

type AnalyzeArgs =
    | [<MainCommand; Unique>] Path of path: string
    | [<AltCommandLine("-o"); Unique>] Output of dir: string
    | Include of projects: string
    | Exclude of projects: string
    | Include_Tests
    | [<AltCommandLine("-f"); Unique>] Framework of tfm: string
    | [<AltCommandLine("-c"); Unique>] Configuration of name: string
    | [<Unique>] Format of OutputFormat
    | Single_File
    | Catalog of file: string
    | Show of categories: string
    | No_Build
    | No_Cache
    | Quiet
    | Silent

    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Path _ -> "the solution (.sln, .slnx) or project to analyze. Default: the solution in the current directory."
            | Output _ -> "output directory. Default: ./floorplan-out"
            | Include _ -> "analyze only these projects (comma-separated) and what they reference."
            | Exclude _ -> "leave out these projects (comma-separated)."
            | Include_Tests -> "include test projects, which are excluded by default."
            | Framework _ -> "preferred target framework for multi-targeting projects."
            | Configuration _ -> "build configuration. Default: Debug"
            | Format _ -> "output format. Default: site"
            | Single_File -> "write the website as one self-contained HTML file."
            | Catalog _ -> "additional boundary catalog rules (JSON)."
            | Show _ -> "show categories hidden by default (comma-separated, e.g. environment,nondeterminism)."
            | No_Build -> "do not build; analyze the assemblies already built."
            | No_Cache -> "do not read or write the analysis cache (FLOORPLAN_CACHE, default in local app data)."
            | Quiet -> "print only errors and the path of the output."
            | Silent -> "print nothing; the exit code says whether the run succeeded."

type Command =
    | [<CliPrefix(CliPrefix.None)>] Analyze of ParseResults<AnalyzeArgs>
    | [<CliPrefix(CliPrefix.DoubleDash)>] Version

    interface IArgParserTemplate with
        member c.Usage =
            match c with
            | Analyze _ -> "analyze a solution and write the website or the analysis model."
            | Version -> "print the version."

type AnalyzeOptions =
    { Path: string option
      Output: string
      Include: string list
      Exclude: string list
      IncludeTests: bool
      Framework: string option
      Configuration: string
      Format: OutputFormat
      SingleFile: bool
      Catalogs: string list
      Show: EffectCategory list
      Build: bool
      Cache: bool
      Quiet: bool
      /// Nothing is printed, errors and the output's path included.
      Silent: bool }

let private splitList (values: string list) =
    values
    |> List.collect (fun v -> v.Split([| ','; ';' |], StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries) |> List.ofArray)

let toOptions (args: ParseResults<AnalyzeArgs>) : Result<AnalyzeOptions, string> =
    let show = args.GetResults Show |> splitList

    let categories, unknown =
        show |> List.map (fun s -> s, EffectCategory.tryParse s) |> List.partition (snd >> Option.isSome)

    if not unknown.IsEmpty then
        let names = EffectCategory.all |> List.map EffectCategory.name |> String.concat ", "
        let bad = unknown |> List.map fst |> String.concat ", "
        Error $"Unknown category: {bad}. Known categories: {names}."
    else
        Ok
            { Path = args.TryGetResult Path
              Output = args.GetResult(Output, defaultValue = "floorplan-out")
              Include = args.GetResults Include |> splitList
              Exclude = args.GetResults Exclude |> splitList
              IncludeTests = args.Contains Include_Tests
              Framework = args.TryGetResult Framework
              Configuration = args.GetResult(Configuration, defaultValue = "Debug")
              Format = args.GetResult(Format, defaultValue = Site)
              SingleFile = args.Contains Single_File
              Catalogs = args.GetResults Catalog
              Show = categories |> List.choose snd
              Build = not (args.Contains No_Build)
              Cache = not (args.Contains No_Cache)
              Quiet = args.Contains Quiet || args.Contains Silent
              Silent = args.Contains Silent }

let defaults =
    { Path = None
      Output = "floorplan-out"
      Include = []
      Exclude = []
      IncludeTests = false
      Framework = None
      Configuration = "Debug"
      Format = Site
      SingleFile = false
      Catalogs = []
      Show = []
      Build = true
      Cache = true
      Quiet = false
      Silent = false }
