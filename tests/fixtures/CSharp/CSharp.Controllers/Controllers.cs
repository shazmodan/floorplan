using Microsoft.AspNetCore.Mvc;

namespace CSharp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    [HttpGet]
    public string List() => "orders";

    [HttpGet("{id:int}")]
    public string Get(int id) => $"order {id}";

    [HttpPost]
    public string Create() => "created";
}

/// Reached by the default conventional route.
public class HomeController : Controller
{
    public string Index() => "home";
}
