using Mixed.Billing.Storage;
using Mixed.Core;

namespace Mixed.Billing;

/// Calls an F# module, and a namespace that calls back into this one: the two namespaces form a cycle.
public class Invoice
{
    public sealed record Line(string Item, decimal Amount);

    public List<Line> Lines { get; } = new();

    public decimal Total => Pricing.withTax(0.25m, Pricing.total(Lines.Select(l => l.Amount)));

    public void Save() => new InvoiceStore().Write(this);
}
