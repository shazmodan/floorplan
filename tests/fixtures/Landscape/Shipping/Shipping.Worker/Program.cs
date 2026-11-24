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
        public static void Delivered(string id) =>
            Http.PutAsync("https://api.example.com/orders/" + id + "/delivered", null).GetAwaiter().GetResult();
    }
}
