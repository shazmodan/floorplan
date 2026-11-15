/// Each effect category, called directly.
module Effects.Direct

open System.Diagnostics
open System.IO
open System.Net.Http

let client = new HttpClient()

let fetch () =
    client.GetStringAsync("https://api.example.org/status").Result

let readConfig () = File.ReadAllText "config.json"

let writeReport (text: string) = File.WriteAllText("report.txt", text)

let runGit () = Process.Start("git", "status") |> ignore
