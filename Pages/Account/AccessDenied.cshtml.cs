using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StudyRoom.Pages.Account;

public class AccessDeniedModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Reason { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet()
    {
        HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
    }

    public void OnPost()
    {
        HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
    }
}

