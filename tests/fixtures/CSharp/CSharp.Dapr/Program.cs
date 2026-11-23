// Dapr: a subscription is a topic the sidecar posts to an endpoint; the client's calls go to the sidecar.
using CSharp.Dapr.Checkout;
using CSharp.Dapr.Payouts;
using Dapr;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddDapr();
var app = builder.Build();
app.UseCloudEvents();
app.MapSubscribeHandler();
app.MapControllers();

// A minimal API endpoint subscribed to a topic, with a convention before the subscription.
app.MapPost("/payouts", Ledger.Record).WithName("payouts").WithTopic("pubsub", "payouts");
app.MapPost("/checkout", Cart.Checkout);
app.Run();

namespace CSharp.Dapr.Payouts
{
    public record Payout(string Id, decimal Amount);

    // The state store.
    public static class Ledger
    {
        public static async Task<IResult> Record(Payout payout, global::Dapr.Client.DaprClient dapr)
        {
            await dapr.SaveStateAsync("ledger", payout.Id, payout);
            var total = await dapr.GetStateAsync<decimal>("ledger", "total");
            await dapr.DeleteStateAsync("ledger", "pending-" + payout.Id);
            return Results.Ok(total);
        }
    }
}

namespace CSharp.Dapr.Receipts
{
    using Microsoft.AspNetCore.Mvc;

    // A controller action subscribed to a topic.
    [ApiController]
    public class ReceiptsController : ControllerBase
    {
        [Topic("pubsub", "receipts")]
        [HttpPost("receipts")]
        public async Task<IActionResult> OnReceipt(CSharp.Dapr.Payouts.Payout receipt)
        {
            await System.IO.File.AppendAllTextAsync("receipts.log", receipt.Id);
            return Ok();
        }
    }
}

namespace CSharp.Dapr.Checkout
{
    using global::Dapr.Client;

    // Publishing, service invocation (by app id and method, by a request, through an HttpClient that invokes an
    // app), an output binding and a secret.
    public static class Cart
    {
        public static async Task<IResult> Checkout(CSharp.Dapr.Payouts.Payout order, DaprClient dapr)
        {
            await dapr.PublishEventAsync("pubsub", "receipts", order);
            var reserved = await dapr.InvokeMethodAsync<CSharp.Dapr.Payouts.Payout, bool>("inventory", "reserve", order);
            await dapr.InvokeMethodAsync(HttpMethod.Post, "shipping", "ship", order);
            var request = dapr.CreateInvokeMethodRequest(HttpMethod.Get, "pricing", "quote");
            await dapr.InvokeMethodAsync(request);
            var billing = DaprClient.CreateInvokeHttpClient("billing");
            await billing.PostAsJsonAsync("/invoices", order);
            await dapr.InvokeBindingAsync("email", "create", order);
            var key = await dapr.GetSecretAsync("vault", "payment-key");
            return Results.Accepted();
        }
    }
}
