/// Effects behind injected functions, interfaces, async and task.
module Effects.HigherOrder

open System.IO
open System.Net.Http

/// Takes the effect as a function; has none itself.
let process' (save: string -> unit) (items: string list) = items |> List.iter save

/// Supplies the effect as a lambda: the file write is inside this module.
let saveAll (items: string list) =
    process' (fun item -> File.AppendAllText("items.txt", item)) items

type IStore =
    abstract Save: string -> unit

type FileStore() =
    interface IStore with
        member _.Save text = File.WriteAllText("store.txt", text)

let store (s: IStore) = s.Save "hello"

let useStore () = store (FileStore())

let fetchAsync (client: HttpClient) =
    async {
        let! body = client.GetStringAsync("https://api.example.org/async") |> Async.AwaitTask
        return body.Length
    }

let readTask () =
    task {
        let! text = File.ReadAllTextAsync "task.txt"
        return text.Length
    }
