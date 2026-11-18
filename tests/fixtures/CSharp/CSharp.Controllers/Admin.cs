using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CSharp.Controllers.Admin;

public interface ILanguageService
{
    void SetLanguage(string language);
}

public class LanguageService : ILanguageService
{
    public string Current { get; private set; } = "en";

    public void SetLanguage(string language) => Current = language;
}

/// A base controller that overrides the platform's action filter (GitHub issue 3).
public class BaseController : Controller
{
    private readonly ILanguageService _languageService;

    public BaseController(ILanguageService languageService)
    {
        _languageService = languageService;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        string language;

        if (Request.Cookies.TryGetValue("culture", out var cookie))
        {
            language = cookie;
        }
        else
        {
            var languages = Request.GetTypedHeaders().AcceptLanguage;
            language = languages.Count > 0 ? languages.First().Value.ToString() : "en";
        }

        _languageService.SetLanguage(language);
        base.OnActionExecuting(context);
    }
}

[Route("")]
public class UsersController : BaseController
{
    public UsersController(ILanguageService languageService) : base(languageService)
    {
    }

    [Route("Users")]
    public IActionResult List()
    {
        if (!IsAuthenticated())
            return RedirectToAction("Login", "Account", new { redirectUri = Request.Path });

        return View();
    }

    private bool IsAuthenticated() => User.Identity?.IsAuthenticated == true;
}
