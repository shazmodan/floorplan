/// Finds the reference assemblies of a .NET version: in the installed SDK's packs folder, or downloaded from
/// NuGet into the cache.
module CatalogDiff.Packs

open System
open System.IO
open System.IO.Compression
open System.Net.Http

let packIds = [ "Microsoft.NETCore.App.Ref"; "Microsoft.AspNetCore.App.Ref" ]

let private dotnetRoot () =
    match Environment.GetEnvironmentVariable "DOTNET_ROOT" with
    | null
    | "" ->
        if OperatingSystem.IsWindows() then
            Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.ProgramFiles, "dotnet")
        else
            "/usr/share/dotnet"
    | root -> root

/// "10.0" -> net10.0; a version is a pack version such as "10.0.15" or "11.0.0".
let tfmOf (version: string) =
    let parts = version.Split('.')
    $"net{parts[0]}.{parts[1]}"

let private refDir (packRoot: string) (version: string) =
    let dir = Path.Combine(packRoot, "ref", tfmOf version)
    if Directory.Exists dir then Some dir else None

let private installed (id: string) (version: string) =
    refDir (Path.Combine(dotnetRoot (), "packs", id, version)) version

let private download (cache: string) (id: string) (version: string) =
    let target = Path.Combine(cache, "packs", id.ToLowerInvariant(), version)

    match refDir target version with
    | Some dir -> Some dir
    | None ->
        let lower = id.ToLowerInvariant()
        let url = $"https://api.nuget.org/v3-flatcontainer/{lower}/{version}/{lower}.{version}.nupkg"
        eprintfn "Downloading %s %s" id version
        use client = new HttpClient()
        let bytes = client.GetByteArrayAsync(url).Result
        use archive = new ZipArchive(new MemoryStream(bytes))
        Directory.CreateDirectory target |> ignore

        for entry in archive.Entries do
            if entry.FullName.StartsWith "ref/" && entry.FullName.EndsWith ".dll" then
                let path = Path.Combine(target, entry.FullName.Replace('/', Path.DirectorySeparatorChar))
                Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
                entry.ExtractToFile(path, true)

        refDir target version

/// The reference folders for a version, from the installed SDK if present, else from NuGet.
let resolve (cache: string) (version: string) : string list =
    packIds
    |> List.choose (fun id ->
        match installed id version with
        | Some dir -> Some dir
        | None -> download cache id version)
