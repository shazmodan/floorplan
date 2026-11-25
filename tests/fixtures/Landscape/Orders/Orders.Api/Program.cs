// The Orders system of the landscape fixture (docs/landscape-plan.md). It calls Billing at a host that names it,
// sends Shipping the orders to ship through a queue Shipping consumes, calls a service outside the landscape, and
// keeps its orders in the ledger database Billing and Shipping connect to, through Entity Framework Core.
using System.Text;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<Orders.Data.OrdersContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Ledger")));
var app = builder.Build();
app.MapGet("/orders/{id}", (string id) => Orders.Api.Invoices.Of(id));
app.MapPost("/orders", (string id) => Orders.Api.Shipments.Ship(id));
app.MapPost("/orders/{id}/log", (Orders.Data.OrdersContext db, string id) => Orders.Data.OrderLog.Record(db, id));
app.MapPut("/orders/{id}/delivered", (string id) => Orders.Api.Stock.Check(id));
app.Run();

namespace Orders.Api
{
    public static class Invoices
    {
        private static readonly HttpClient Http = new();

        // The host names the Billing system: Billing's GET /invoices/{id}.
        public static Task<string> Of(string id) => Http.GetStringAsync("https://billing-api.internal/invoices/" + id);
    }

    public static class Shipments
    {
        // The queue Shipping consumes, through the same library.
        public static void Ship(string id)
        {
            var channel = new ConnectionFactory { HostName = "localhost" }.CreateConnection().CreateModel();
            channel.BasicPublish("", "shipments", null, Encoding.UTF8.GetBytes(id));
        }
    }

    public static class Stock
    {
        private static readonly HttpClient Http = new();

        // No system of the landscape has the route: an external system.
        public static Task<string> Check(string id) => Http.GetStringAsync("https://inventory.example.net/stock/" + id);
    }
}
