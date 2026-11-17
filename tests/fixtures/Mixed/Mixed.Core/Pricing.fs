/// An F# module that C# code calls.
module Mixed.Core.Pricing

let total (lines: decimal seq) = Seq.sum lines

let withTax (rate: decimal) (amount: decimal) = amount * (1m + rate)
