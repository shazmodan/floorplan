using System.ServiceModel;
using CSharp.WcfClient;

// A channel made from the contract.
var factory = new ChannelFactory<IOrderService>(new BasicHttpBinding(), new EndpointAddress("http://localhost:5000/OrderService.svc"));
var orders = factory.CreateChannel();
Console.WriteLine(orders.Place("book", 2));
await orders.CancelOrderAsync("book-2");

// A proxy written by hand.
var query = new StockQueryClient(new BasicHttpBinding(), new EndpointAddress("http://localhost:5000/stock"));
Console.WriteLine(query.Count("book"));

// A proxy as dotnet-svcutil writes it.
var stock = new StockServiceClient(new BasicHttpBinding(), new EndpointAddress("http://localhost:5000/stock"));
Console.WriteLine(await stock.ReserveAsync("book"));

namespace CSharp.WcfClient
{
    [ServiceContract]
    public interface IOrderService
    {
        [OperationContract]
        string Place(string item, int quantity);

        // The service's CancelAsync, renamed CancelOrder: its action is .../IOrderService/CancelOrder.
        [OperationContract]
        Task CancelOrderAsync(string id);
    }

    [ServiceContract(Name = "IStockQuery", Namespace = "http://example.org/stock")]
    public interface IStockQueryContract
    {
        [OperationContract]
        int Count(string item);
    }

    public class StockQueryClient : ClientBase<IStockQueryContract>, IStockQueryContract
    {
        public StockQueryClient(System.ServiceModel.Channels.Binding binding, EndpointAddress address) : base(binding, address) { }

        public int Count(string item) => Channel.Count(item);
    }

    [System.CodeDom.Compiler.GeneratedCode("Microsoft.Tools.ServiceModel.Svcutil", "2.1.0")]
    [ServiceContract(Namespace = "http://example.org/stock", ConfigurationName = "Stock.IStockService")]
    public interface IStockService
    {
        [OperationContract(Action = "urn:stock:reserve", ReplyAction = "urn:stock:reserveResponse")]
        Task<int> ReserveAsync(string item);
    }

    [System.Diagnostics.DebuggerStepThrough]
    [System.CodeDom.Compiler.GeneratedCode("Microsoft.Tools.ServiceModel.Svcutil", "2.1.0")]
    public partial class StockServiceClient : ClientBase<IStockService>, IStockService
    {
        public StockServiceClient(System.ServiceModel.Channels.Binding binding, EndpointAddress address) : base(binding, address) { }

        public Task<int> ReserveAsync(string item) => Channel.ReserveAsync(item);
    }
}
