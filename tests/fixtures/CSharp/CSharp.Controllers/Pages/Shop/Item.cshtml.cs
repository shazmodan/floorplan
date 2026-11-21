using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CSharp.Controllers.Pages.Shop;

// The page's template ("{id:int}") follows its path; a named handler is chosen by the handler query value.
public class ItemModel : PageModel
{
    public Task OnGetAsync(int id) => System.IO.File.AppendAllTextAsync("items.log", id.ToString());

    public IActionResult OnPostRemove(int id)
    {
        System.IO.File.Delete($"items/{id}");
        return RedirectToPage("Index");
    }
}
