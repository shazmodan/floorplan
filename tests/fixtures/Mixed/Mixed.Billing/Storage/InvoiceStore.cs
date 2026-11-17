namespace Mixed.Billing.Storage
{
    /// A block-scoped namespace that uses the namespace above it and a type in no namespace.
    public class InvoiceStore
    {
        public void Write(Invoice invoice) =>
            File.WriteAllText(Path.Combine(BillingDefaults.Folder, "invoice.txt"), invoice.Total.ToString());

        /// An effect inside a lambda inside an async method: C# closures and state machines.
        public async Task ArchiveAsync(IEnumerable<Invoice> invoices)
        {
            await Task.Yield();
            invoices.ToList().ForEach(i => File.AppendAllText("archive.txt", i.Total.ToString()));
        }
    }
}
