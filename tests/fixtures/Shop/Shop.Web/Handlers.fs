module Shop.Web.Handlers

open System.Net.Http
open Shop.Core
open Shop.Data

let listOrders () =
    Repository.load () |> Orders.summarize |> String.concat "\n"

type FileNotifier() =
    interface Orders.INotifier with
        member _.Notify(message: string) = System.IO.File.AppendAllText("notices.txt", message)

let createOrder (item: string) =
    let order = Domain.create 0 item 1
    Repository.save order
    Orders.announce (FileNotifier()) order
    "created"

let private client = new HttpClient()

let price (item: string) =
    client.GetStringAsync("https://prices.example.org/items/" + item)
