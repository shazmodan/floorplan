// The Shipping system of the landscape fixture (docs/landscape-plan.md). It consumes the queue Orders sends to, and
// tells Orders an order was delivered at a host the landscape file maps to Orders.
using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

var channel = new ConnectionFactory { HostName = "localhost" }.CreateConnection().CreateModel();
var shipments = new EventingBasicConsumer(channel);
shipments.Received += (_, e) => Shipping.Worker.Delivery.Delivered(Encoding.UTF8.GetString(e.Body.ToArray()));
channel.BasicConsume("shipments", true, shipments);
await Task.Delay(Timeout.Infinite);

namespace Shipping.Worker
{
    public static class Delivery
    {
        private static readonly HttpClient Http = new();

        // api.example.com names no system; floorplan.landscape.json maps it to Orders.
        public static void Delivered(string id)
        {
            Http.PutAsync("https://api.example.com/orders/" + id + "/delivered", null).GetAwaiter().GetResult();
            // The folder Billing writes to: shared, so the systems integrate through it too.
            File.AppendAllText("/srv/exchange/delivered.csv", id);

            // The database Billing uses, written another way (case, key names, another user and password).
            using var connection = new Npgsql.NpgsqlConnection("Server=DB.internal;Port=5432;Database=Ledger;User Id=shipping;Password=other");
            connection.Open();
        }

        // The stock database Orders keeps through a context of its own.
        public static void Packed()
        {
            using var connection = new Npgsql.NpgsqlConnection("Host=stock-db.internal;Database=stock;Username=shipping");
            connection.Open();
        }

        // Dapper opens this connection itself: the command goes to the stock database, although the project
        // connects to two.
        public static int Restock(string sku)
        {
            using var connection = new Npgsql.NpgsqlConnection("Host=stock-db.internal;Database=stock;Username=shipping;Password=secret");
            return Dapper.SqlMapper.Execute(connection, "UPDATE stock SET count = count + 1 WHERE sku = @sku", new { sku });
        }
    }
}
