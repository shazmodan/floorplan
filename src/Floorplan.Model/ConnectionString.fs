/// Database connection strings ("Server=db1;Database=Orders;User Id=app;Password=..."), as ADO.NET providers write
/// them: what an exit point may show of one, and the database it names.
module Floorplan.Model.ConnectionString

open System

/// The pairs of a connection string, in order: `key=value` separated by `;`, a value optionally in quotes.
let private pairs (text: string) =
    text.Split ';'
    |> Array.choose (fun part ->
        match part.IndexOf '=' with
        | -1 -> None
        | i ->
            let key = part.Substring(0, i).Trim()
            let value = part.Substring(i + 1).Trim().Trim('"', '\'')
            if key = "" then None else Some(key, value))
    |> Array.toList

/// Whether a key holds a secret: a password, an account or access key, a token.
let private secret (key: string) =
    let k = key.Replace(" ", "").ToLowerInvariant()

    [ "password"; "pwd"; "secret"; "token"; "accountkey"; "sharedaccesskey"; "accesskey" ]
    |> List.exists k.Contains

/// The connection string with each secret's value replaced by "…", so nothing shown or written holds it.
let redact (text: string) =
    if not (text.Contains '=') then
        text
    else
        text.Split ';'
        |> Array.map (fun part ->
            match part.IndexOf '=' with
            | -1 -> part
            | i when secret (part.Substring(0, i)) -> part.Substring(0, i + 1) + "…"
            | _ -> part)
        |> String.concat ";"

let private serverKeys = [ "server"; "data source"; "datasource"; "host"; "address"; "addr"; "network address" ]
let private databaseKeys = [ "database"; "initial catalog"; "db" ]

/// Servers that are each process's own machine: no two systems share a database there.
let private local (server: string) =
    server = "localhost"
    || server = "127.0.0.1"
    || server = "::1"
    || server = "."
    || server = "(local)"
    || server.StartsWith "(localdb)"
    || server.StartsWith "localhost:"
    || server.StartsWith "127.0.0.1:"

/// The database a connection string names, as a key that is the same for every connection to it (server, with
/// its port, and database, in lower case) and a name to show ("Orders on db1"): only with both a server and a
/// database, on a server that is not the local machine, and with no part known only at run time.
let database (text: string) : (string * string) option =
    if text.Contains "{…}" then
        None
    else
        let ps = pairs text
        let find (keys: string list) =
            ps |> List.tryFind (fun (k, _) -> keys |> List.contains (k.ToLowerInvariant())) |> Option.map snd |> Option.filter (fun v -> v <> "")

        match find serverKeys, find databaseKeys with
        | Some server, Some db ->
            let server =
                let s = server.Trim().ToLowerInvariant()
                let s = if s.StartsWith "tcp:" then s.Substring 4 else s
                // SQL Server writes the port after a comma, others in a key of their own.
                let s = s.Replace(',', ':')

                match find [ "port" ] with
                | Some port when not (s.Contains ':') -> $"{s}:{port.Trim()}"
                | _ -> s

            if local server then None
            else Some($"{server}/{db.ToLowerInvariant()}", $"{db} on {server}")
        | _ -> None
