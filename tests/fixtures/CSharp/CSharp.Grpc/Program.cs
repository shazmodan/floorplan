// A gRPC service: each method it overrides is POST /{package.Service}/{Method}; one it leaves to the generated base
// class answers that it is unimplemented, and is no entry point.
using CSharp.Grpc.Inventory;
using Grpc.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();

var app = builder.Build();
app.MapGrpcService<StockService>();
app.Run();

namespace CSharp.Grpc.Inventory
{
    public class StockService : Stock.StockBase
    {
        public override async Task<ReserveReply> Reserve(ReserveRequest request, ServerCallContext context)
        {
            await File.AppendAllTextAsync("reservations.log", request.Sku);
            return new ReserveReply { Ok = true };
        }
    }
}
