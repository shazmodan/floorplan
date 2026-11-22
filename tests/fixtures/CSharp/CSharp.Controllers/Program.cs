// MVC controllers in C#: attribute routes and the default conventional route; Razor Pages, two with a route of a convention.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddRazorPages(o => o.Conventions.AddPageRoute("/Shop/Index", "home").AddAreaPageRoute("Admin", "/Audit", "audit/{year:int?}"));

var app = builder.Build();
app.MapControllers();
app.MapDefaultControllerRoute();
app.MapRazorPages();
app.Run();
