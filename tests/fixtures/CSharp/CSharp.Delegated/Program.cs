// CSharp.Middleware with its router in a dependency: only the handlers are user code, passed as method groups.
using CSharp.Delegated;
using CSharp.Library;

var handlers = new Handlers(
    Health: Orders.Health,
    CreateOrder: Orders.Create,
    ShowOrder: Orders.Show,
    RemoveOrder: Orders.Remove,
    NotFound: Orders.NotFound);

var app = WebApplication.CreateBuilder(args).Build();
app.Run(ctx => Router.Route(handlers, ctx));
app.Run();
