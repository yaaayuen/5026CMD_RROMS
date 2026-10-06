using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Services;

namespace Rroms.Web.Pages.Account;

/// <summary>
/// Signs the user out. Owner: YY (YY-2)
/// /// Signing out only happens on POST, which requires the antiforgery token from our own pages.
/// If GET signed users out, any website could log our users out with a hidden image or link
/// </summary>

public class LogoutModel : PageModel
{
    private readonly IAccountServices _accountService;

    //Receives the account service from dependency injection
    public LogoutModel(IAccountServices accountService)
    {
        _accountService = accountService;
    }

    //GET request does not sign out; it just returns to home page
    public IActionResult OnGet()
    {
        return RedirectToPage("/Index");
    }

    //Removes rhe authentication cookie and redirects to home page. This is a POST action
    public async Task<IActionResult> OnPostAsync()
    {
        await _accountService.SignOutAsync();
        TempData["StatusMessage"] = "You have been signed out. See you soon!";
        return RedirectToPage("/Index");
    }
}