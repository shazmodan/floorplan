using CoreWCF;
using CoreWCF.Configuration;
using CSharp.Wcf.Orders;
using CSharp.Wcf.Stock;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddServiceModelServices();
builder.Services.AddSingleton<OrderStore>();
var app = builder.Build();

app.UseServiceModel(services =>
{
    services.AddService<OrderService>();
    services.AddServiceEndpoint<OrderService, IOrderService>(new BasicHttpBinding(), "/OrderService.svc");
    services.AddService<StockService>();
    services.AddServiceEndpoint<StockService, IStockService>(new BasicHttpBinding(), "/stock");
    services.AddServiceEndpoint<StockService, IStockService>(new WSHttpBinding(SecurityMode.None), "/stock/ws");
});

app.Run();

namespace CSharp.Wcf.Orders
{
    [ServiceContract]
    public interface IOrderService
    {
        [OperationContract]
        string Place(string item, int quantity);

        // Renamed: clients call it as CancelOrder.
        [OperationContract(Name = "CancelOrder")]
        Task CancelAsync(string id);

        // Not an operation: no client can call it.
        string Describe();
    }

    public class OrderStore
    {
        public Task Save(string id) => File.WriteAllTextAsync($"orders/{id}.txt", id);
    }

    public class OrderService : IOrderService
    {
        private readonly OrderStore _store;

        public OrderService(OrderStore store) => _store = store;

        public string Place(string item, int quantity)
        {
            var id = $"{item}-{quantity}";
            _store.Save(id).Wait();
            return id;
        }

        public Task CancelAsync(string id) => File.WriteAllTextAsync($"cancelled/{id}.txt", id);

        public string Describe() => "orders";
    }
}

namespace CSharp.Wcf.Stock
{
    [ServiceContract(Namespace = "http://example.org/stock")]
    public interface IStockQuery
    {
        [OperationContract]
        int Count(string item);
    }

    // A contract that derives from another: the service exposes both's operations.
    [ServiceContract(Namespace = "http://example.org/stock")]
    public interface IStockService : IStockQuery
    {
        [OperationContract(Action = "urn:stock:reserve")]
        Task<int> ReserveAsync(string item);
    }

    public class StockService : IStockService
    {
        public int Count(string item) => item.Length;

        Task<int> IStockService.ReserveAsync(string item) => Task.FromResult(File.ReadAllText($"stock/{item}.txt").Length);
    }
}
