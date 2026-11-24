// The Billing system of the landscape fixture (docs/landscape-plan.md). Orders calls its invoices; it calls Orders
// at an address that names no system, by a route only Orders has.
var app = WebApplication.CreateBuilder(args).Build();
app.MapGet("/invoices/{id}", (string id) => "invoice " + id);
app.MapDelete("/invoices/{id}", (string id) => Billing.Api.Ledger.Forget(id));
app.MapGet("/invoices/{id}/order", (string id) => Billing.Api.Orders.Of(id));
app.MapGet("/health", () => "ok");
app.Run();

namespace Billing.Api
{
    public static class Orders
    {
        private static readonly HttpClient Http = new();

        // An address that names nothing: matched by the route, which only Orders has (probable).
        public static Task<string> Of(string id) => Http.GetStringAsync("http://10.0.0.7/orders/" + id);
    }

    public static class Ledger
    {
        public static void Forget(string id) => File.AppendAllText("forgotten.log", id);
    }
}
