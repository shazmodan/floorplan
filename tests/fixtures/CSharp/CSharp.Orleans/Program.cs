// Orleans: a client calls a grain by its interface; the silo activates the grain and runs the call.
using CSharp.Silo.Orders;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseOrleans(silo =>
{
    silo.UseLocalhostClustering();
    silo.AddMemoryGrainStorage("orders");
});
builder.Services.AddHttpClient();
var app = builder.Build();

app.MapPost("/orders/{id}", async (string id, int quantity, IGrainFactory grains) =>
    Results.Ok(await grains.GetGrain<IOrderGrain>(id).Place(quantity)));
app.MapDelete("/orders/{id}", async (string id, IClusterClient client) =>
{
    await client.GetGrain<IOrderGrain>(id).Cancel();
    return Results.NoContent();
});
app.Run();

namespace CSharp.Silo.Orders
{
    [GenerateSerializer]
    public class OrderState
    {
        [Id(0)] public int Quantity { get; set; }
    }

    public interface IOrderGrain : IGrainWithStringKey
    {
        Task<int> Place(int quantity);
        Task Cancel();
    }

    // A grain: its state is persisted in a storage provider, and it calls another grain and an HTTP service.
    public class OrderGrain : Grain, IOrderGrain
    {
        private readonly IPersistentState<OrderState> _state;
        private readonly HttpClient _http;

        public OrderGrain([PersistentState("order", "orders")] IPersistentState<OrderState> state, IHttpClientFactory http)
        {
            _state = state;
            _http = http.CreateClient();
        }

        public async Task<int> Place(int quantity)
        {
            _state.State.Quantity += quantity;
            await _state.WriteStateAsync();
            await GrainFactory.GetGrain<CSharp.Silo.Stock.IStockGrain>(0).Reserve(quantity);
            await _http.PostAsync("https://payments.example.org/charge", null);
            return _state.State.Quantity;
        }

        public async Task Cancel()
        {
            await _state.ClearStateAsync();
            await File.AppendAllTextAsync("cancelled.log", this.GetPrimaryKeyString() + "\n");
        }
    }
}

namespace CSharp.Silo.Stock
{
    public interface IStockGrain : IGrainWithIntegerKey
    {
        Task Reserve(int quantity);
    }

    // A grain with a timer the silo runs while it is active.
    public class StockGrain : Grain, IStockGrain
    {
        private int _reserved;

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            this.RegisterGrainTimer(Flush, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            return base.OnActivateAsync(cancellationToken);
        }

        public Task Reserve(int quantity)
        {
            _reserved += quantity;
            return Task.CompletedTask;
        }

        private Task Flush() => File.AppendAllTextAsync("stock.log", _reserved + "\n");
    }
}
