module Shop.Data.Repository

open System.IO
open Shop.Core.Domain

let load () : Order list =
    if File.Exists Util.ordersFile then
        File.ReadAllLines Util.ordersFile
        |> Array.mapi (fun i line -> create i line 1)
        |> Array.toList
    else
        []

let save (order: Order) =
    Shop.Storage.Archive.Append(Util.ordersFile, order.Item)
