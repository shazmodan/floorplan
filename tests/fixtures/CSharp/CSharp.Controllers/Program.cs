// MVC controllers in C#: attribute routes and the default conventional route.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.MapDefaultControllerRoute();
app.Run();
