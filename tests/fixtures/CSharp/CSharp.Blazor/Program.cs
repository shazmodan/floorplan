// Razor components: MapRazorComponents maps the routed components of the root component's assembly, and
// AddInteractiveServerRenderMode lets interactive ones handle the browser's events over a circuit.
using CSharp.Blazor.Components;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
