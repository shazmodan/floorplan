// A C# application with top-level statements, whose code is in the global namespace.
using Mixed.Billing;

var invoice = new Invoice();
invoice.Lines.Add(new Invoice.Line("coffee", 3m));
invoice.Save();
Console.WriteLine(invoice.Total);
