/// An F# application using a C# library.
module Mixed.App.Program

open Mixed.Billing

[<EntryPoint>]
let main _ =
    let invoice = Invoice()
    invoice.Lines.Add(Invoice.Line("tea", 4m))
    invoice.Save()
    0
