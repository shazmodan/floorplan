using Microsoft.AspNetCore.Mvc;

namespace CSharp.Controllers;

/// A controller with views that use ViewBag, which is C# dynamic (GitHub issue 2): writing to it is not a blind spot.
public class PagesController : Controller
{
    public IActionResult About()
    {
        ViewBag.Title = "About";
        ViewBag.Visits = 3;
        return View();
    }

    /// A dynamic value that is not ViewBag stays a blind spot.
    public string Describe(dynamic value) => value.Name;
}
