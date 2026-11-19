// Controllers mapped only by a conventional route (no MapControllers): MVC still maps their attribute routes.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();

var app = builder.Build();
app.UseRouting();
app.MapControllerRoute(
    name: "RouteTable",
    pattern: "{controller=Route}/{action=Main}/{id?}");
app.Run();
