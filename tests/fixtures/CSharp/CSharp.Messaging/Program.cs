// Message consumers: RabbitMQ and Azure Service Bus call the handlers registered here for each message they
// receive. The queue is named where the consumer is made or tied to it.
using System.Text;
using Azure.Messaging.ServiceBus;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

CSharp.Messaging.Shipping.Bus.Configure(new Microsoft.Extensions.DependencyInjection.ServiceCollection());
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

namespace CSharp.Messaging.Shipping
{
    using MassTransit;

    public record OrderShipped(string OrderId);

    // MassTransit: the bus calls a registered consumer's Consume for each message of its type.
    public class ShippedConsumer : IConsumer<OrderShipped>
    {
        public Task Consume(ConsumeContext<OrderShipped> context) => File.AppendAllTextAsync("shipped.log", context.Message.OrderId);
    }

    // Never registered: not called by the bus.
    public class IdleConsumer : IConsumer<OrderShipped>
    {
        public Task Consume(ConsumeContext<OrderShipped> context) => Task.CompletedTask;
    }

    public static class Bus
    {
        public static void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services) =>
            services.AddMassTransit(x =>
            {
                x.AddConsumer<ShippedConsumer>();
                x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
            });
    }
}

namespace CSharp.Messaging.Publishing
{
    using MassTransit;

    // Sending messages: each call that sends is an exit point of category network, with the routing key where the
    // call has one.
    public class Publisher(IModel channel, ServiceBusSender sender, IPublishEndpoint bus)
    {
        public void Order(string id) => channel.BasicPublish("", "orders", null, Encoding.UTF8.GetBytes(id));

        public Task Payment(string id) => sender.SendMessageAsync(new ServiceBusMessage(id));

        public Task Shipped(string id) => bus.Publish(new Shipping.OrderShipped(id));
    }
}

namespace CSharp.Messaging.Refunds
{
    // Receiving in a loop of its own: the method that receives is the handler.
    public static class Poller
    {
        public static async Task RunAsync(ServiceBusClient client, CancellationToken ct)
        {
            var receiver = client.CreateReceiver("refunds");

            while (!ct.IsCancellationRequested)
            {
                var message = await receiver.ReceiveMessageAsync(cancellationToken: ct);
                await File.AppendAllTextAsync("refunds.log", message.Body.ToString(), ct);
            }
        }

        public static void Drain(IModel channel)
        {
            while (channel.BasicGet("audit", true) is { } result)
                File.AppendAllText("audit.log", Encoding.UTF8.GetString(result.Body.ToArray()));
        }
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
