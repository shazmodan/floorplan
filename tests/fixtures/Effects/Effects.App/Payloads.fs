/// Payload recovery: literals, templates, and values that stay dynamic.
module Effects.Payloads

open System.IO
open System.Net.Http

[<Literal>]
let DataDir = "data"

let client = new HttpClient()

let user (id: string) =
    client.GetStringAsync($"https://api.example.org/users/{id}").Result

let order (id: int) =
    client.GetStringAsync(sprintf "https://api.example.org/orders/%d" id).Result

let file (name: string) = File.ReadAllText(Path.Combine(DataDir, name))

let anything (path: string) = File.ReadAllText path
