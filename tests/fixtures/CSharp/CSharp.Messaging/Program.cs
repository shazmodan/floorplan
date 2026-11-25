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
    public class Publisher(IModel channel, ServiceBusSender sender, IPublishEndpoint bus, Confluent.Kafka.IProducer<string, string> producer)
    {
        public void Order(string id) => channel.BasicPublish("", "orders", null, Encoding.UTF8.GetBytes(id));

        public Task Payment(string id) => sender.SendMessageAsync(new ServiceBusMessage(id));

        public Task Shipped(string id) => bus.Publish(new Shipping.OrderShipped(id));

        // Kafka: the topic is the first argument.
        public Task Placed(string id) => producer.ProduceAsync("placed", new Confluent.Kafka.Message<string, string> { Key = id, Value = id });

        public void Cancelled(string id) => producer.Produce("cancelled", new Confluent.Kafka.Message<string, string> { Key = id, Value = id });
    }
}

namespace CSharp.Messaging.Sales
{
    using NServiceBus;

    public record PlaceOrder(string Id) : ICommand;

    // NServiceBus: the endpoint finds its handlers by scanning the application; Handle runs for each message.
    public class PlaceOrderHandler : IHandleMessages<PlaceOrder>
    {
        public Task Handle(PlaceOrder message, IMessageHandlerContext context) => File.AppendAllTextAsync("sales.log", message.Id);
    }

    public static class SalesEndpoint
    {
        public static async Task StartAsync()
        {
            var config = new EndpointConfiguration("Sales");
            config.UseTransport(new LearningTransport());
            config.UseSerialization<SystemJsonSerializer>();
            var endpoint = await Endpoint.Start(config);
            await endpoint.Send("Sales", new PlaceOrder("1"));
        }
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

        // Kafka: a loop that consumes receives from the topic the consumer subscribed to.
        public static void Consume(CancellationToken ct)
        {
            var config = new Confluent.Kafka.ConsumerConfig { BootstrapServers = "localhost:9092", GroupId = "refunds" };
            using var consumer = new Confluent.Kafka.ConsumerBuilder<string, string>(config).Build();
            consumer.Subscribe("refunds-requested");

            while (!ct.IsCancellationRequested)
            {
                var result = consumer.Consume(ct);
                File.AppendAllText("requested.log", result.Message.Value);
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

namespace CSharp.Messaging.Configured
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;

    public class AuditQueue
    {
        public string Name { get; set; } = "";
    }

    // Queues named in appsettings.json: read by key, through a section, and through an options class bound to one.
    public class Invoices(IConfiguration configuration, IOptions<AuditQueue> audit)
    {
        public static void Configure(IServiceCollection services, IConfiguration configuration) =>
            services.Configure<AuditQueue>(configuration.GetSection("Queues:Audit"));

        public void Start(IModel channel)
        {
            var invoices = new EventingBasicConsumer(channel);
            invoices.Received += (_, e) => File.AppendAllText("invoices.log", Encoding.UTF8.GetString(e.Body.ToArray()));
            channel.BasicConsume(configuration["Queues:Invoices"], true, invoices);

            var credits = new EventingBasicConsumer(channel);
            credits.Received += (_, e) => File.AppendAllText("credits.log", e.RoutingKey);
            channel.BasicConsume(configuration.GetSection("Queues").GetSection("Credits").Value, true, credits);

            var reminders = new EventingBasicConsumer(channel);
            reminders.Received += (_, e) => File.AppendAllText("reminders.log", e.RoutingKey);
            channel.BasicConsume(configuration.GetValue<string>("Queues:Reminders"), true, reminders);

            var audits = new EventingBasicConsumer(channel);
            audits.Received += (_, e) => File.AppendAllText("audits.log", e.RoutingKey);
            channel.BasicConsume(audit.Value.Name, true, audits);
        }

        // A queue the configuration does not name: unresolved.
        public void Missing(IModel channel) =>
            channel.BasicPublish("", configuration["Queues:Missing"], null, Encoding.UTF8.GetBytes("x"));

        public void Notify(IModel channel) =>
            channel.BasicPublish("", configuration["Queues:Invoices"], null, Encoding.UTF8.GetBytes("x"));
    }

    public class RabbitOptions
    {
        public QueueNames Queues { get; set; } = new();
    }

    public class QueueNames
    {
        public string Settlements { get; set; } = "";
    }

    // A queue named by a nested options object, kept in a field when the consumer is made.
    public class Settlements
    {
        private readonly RabbitOptions _options;

        public Settlements(IOptions<RabbitOptions> options) => _options = options.Value;

        public static void Configure(IServiceCollection services, IConfiguration configuration) =>
            services.Configure<RabbitOptions>(configuration.GetSection("Rabbit"));

        public void Start(IModel channel)
        {
            var settlements = new EventingBasicConsumer(channel);
            settlements.Received += (_, e) => File.AppendAllText("settlements.log", e.RoutingKey);
            channel.BasicConsume(_options.Queues.Settlements, true, settlements);
        }

        public void Notify(IModel channel) =>
            channel.BasicPublish("", _options.Queues.Settlements, null, Encoding.UTF8.GetBytes("x"));
    }
}
