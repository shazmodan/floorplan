// MVC controllers in C#: attribute routes and the default conventional route.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddRazorPages();

var app = builder.Build();
app.MapControllers();
app.MapDefaultControllerRoute();
app.MapRazorPages();
app.Run();
