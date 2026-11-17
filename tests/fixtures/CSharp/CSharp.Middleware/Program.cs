using CSharp.Middleware;

var app = WebApplication.CreateBuilder(args).Build();
app.Run(Router.Route);
app.Run();
