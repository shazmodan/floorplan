module Shop.Worker.Program

open System.Diagnostics
open Shop.Core

[<EntryPoint>]
let main _ =
    let order = Domain.create 1 "report" 1
    printfn "%s" (Util.describe order)
    Process.Start("echo", "done") |> ignore
    0
