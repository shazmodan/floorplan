module Shop.Worker.Program

open System.Diagnostics
open Shop.Core

type ProcessNotifier() =
    interface Orders.INotifier with
        member _.Notify(message: string) = Process.Start("notify", message) |> ignore

[<EntryPoint>]
let main _ =
    let order = Domain.create 1 "report" 1
    Orders.announce (ProcessNotifier()) order
    printfn "%s" (Util.describe order)
    Process.Start("echo", "done") |> ignore
    0
