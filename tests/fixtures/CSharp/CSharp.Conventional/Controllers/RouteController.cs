using Microsoft.AspNetCore.Mvc;

namespace CSharp.Conventional.Controllers;

/// Attribute routed: the conventional route reaches none of these (/Route/Main is a 404).
[Route("")]
public class RouteController : Controller
{
    public IActionResult Main() => Respond();

    [Route("Apa")]
    public IActionResult A01() => Respond();

    [Route("Bepa")]
    public IActionResult A02() => Respond();

    [HttpPost("Herpa")]
    public IActionResult A04() => Respond();

    [HttpGet("r/Derpa")]
    public IActionResult A15() => Respond();

    private ContentResult Respond() => Content("ok", "text/plain; charset=utf-8");
}

/// No routing attributes: reached by the conventional route.
public class HomeController : Controller
{
    public IActionResult Index() => Content("home");
}
