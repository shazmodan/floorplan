// Minimal APIs in C#: method groups, lambdas, groups handed to a method, and a template built at run time.
using CSharp.Endpoints;

var app = CSharp.Endpoints.Shipping.Setup.Configure(WebApplication.CreateBuilder(args)).Build();

app.MapGet("/api/orders", Api.ListOrders);
app.MapGet("/api/orders/{id:int}", (int id) => Api.GetOrder(id));
app.MapPost("/api/orders", Api.CreateOrder);
app.MapDelete("/api/orders/{id:int}", async (int id) => await Api.DeleteOrderAsync(id));
app.MapMethods("/health", new[] { "GET", "HEAD" }, () => "ok");

app.MapPost("/api/audit", CSharp.Endpoints.Auditing.Audit.Cancelled);

Admin.Map(app.MapGroup("/admin"));
app.MapGet(Environment.GetEnvironmentVariable("STATUS_PREFIX") + "/status", () => "up");

app.Run();
