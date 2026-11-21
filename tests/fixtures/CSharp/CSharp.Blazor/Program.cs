// Razor components: MapRazorComponents maps the routed components of the root component's assembly.
using CSharp.Blazor.Components;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents();

var app = builder.Build();
app.UseAntiforgery();
app.MapRazorComponents<App>();
app.Run();
