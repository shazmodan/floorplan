using Microsoft.AspNetCore.Mvc.RazorPages;

// Razor Pages: the page model's On{Verb} methods handle the page's requests, at the route its file gives it.
namespace CSharp.Controllers.Pages.Shop;

public class IndexModel : PageModel
{
    public void OnGet() => System.IO.File.AppendAllText("shop.log", "visit");
}
