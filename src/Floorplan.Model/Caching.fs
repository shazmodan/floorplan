/// On-disk caches shared by the analysis stages (plan sections 5 and risk 9). Entries are JSON files named by a
/// content hash; a missing, stale or unreadable entry is simply recomputed.
module Floorplan.Model.Caching

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Text.Json

/// FLOORPLAN_CACHE, or the user's local application data folder.
let defaultDirectory () =
    match Environment.GetEnvironmentVariable "FLOORPLAN_CACHE" with
    | null
    | "" ->
        let root = Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData
        let root = if String.IsNullOrEmpty root then Path.GetTempPath() else root
        Path.Combine(root, "floorplan", "cache")
    | dir -> dir

let hash (parts: string seq) =
    use sha = SHA256.Create()
    let bytes = Encoding.UTF8.GetBytes(String.Join("\u001f", parts))
    Convert.ToHexString(sha.ComputeHash bytes).Substring(0, 32).ToLowerInvariant()

let fileHash (path: string) =
    use sha = SHA256.Create()
    use stream = File.OpenRead path
    Convert.ToHexString(sha.ComputeHash stream).ToLowerInvariant()

type Store(directory: string option) =
    let options = Json.options false

    member _.Enabled = directory.IsSome

    member _.TryRead<'T>(kind: string, key: string) : 'T option =
        match directory with
        | None -> None
        | Some dir ->
            let file = Path.Combine(dir, kind, key + ".json")

            if File.Exists file then
                try
                    Some(JsonSerializer.Deserialize<'T>(File.ReadAllText file, options))
                with _ ->
                    None
            else
                None

    member _.Write<'T>(kind: string, key: string, value: 'T) =
        match directory with
        | None -> ()
        | Some dir ->
            try
                let folder = Path.Combine(dir, kind)
                Directory.CreateDirectory folder |> ignore
                let file = Path.Combine(folder, key + ".json")
                let temp = file + "." + string (Guid.NewGuid()) + ".tmp"
                File.WriteAllText(temp, JsonSerializer.Serialize(value, options))
                File.Move(temp, file, true)
            with _ ->
                // A cache that cannot be written only costs time.
                ()

    static member Disabled = Store None
