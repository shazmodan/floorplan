// Message consumers: RabbitMQ and Azure Service Bus call the handlers registered here for each message they
// receive. The queue is named where the consumer is made or tied to it.
using System.Text;
using Azure.Messaging.ServiceBus;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

CSharp.Messaging.Orders.Listener.Start();
await CSharp.Messaging.Payments.Listener.StartAsync();
await Task.Delay(Timeout.Infinite);

namespace CSharp.Messaging.Orders
{
    public static class Listener
    {
        public static void Start()
        {
            var factory = new ConnectionFactory { HostName = "localhost" };
            var channel = factory.CreateConnection().CreateModel();

            var orders = new EventingBasicConsumer(channel);
            orders.Received += (_, e) => File.AppendAllText("orders.log", Encoding.UTF8.GetString(e.Body.ToArray()));
            channel.BasicConsume("orders", true, orders);

            // The consumer first: another overload.
            var returns = new EventingBasicConsumer(channel);
            returns.Received += OnReturn;
            channel.BasicConsume(returns, "returns");
        }

        private static void OnReturn(object? sender, BasicDeliverEventArgs e) => File.Delete("pending/" + e.DeliveryTag);
    }
}

namespace CSharp.Messaging.Payments
{
    public static class Listener
    {
        public static async Task StartAsync()
        {
            var client = new ServiceBusClient("Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=s");
            var processor = client.CreateProcessor("payments");
            processor.ProcessMessageAsync += Handle;
            processor.ProcessErrorAsync += _ => Task.CompletedTask;
            await processor.StartProcessingAsync();
        }

        private static async Task Handle(ProcessMessageEventArgs args)
        {
            await File.AppendAllTextAsync("payments.log", args.Message.Body.ToString());
            await args.CompleteMessageAsync(args.Message);
        }
    }
}
