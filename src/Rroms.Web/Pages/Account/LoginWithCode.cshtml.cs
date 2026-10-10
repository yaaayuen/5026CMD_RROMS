using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.RateLimiting;
using Rroms.Web.Common;
using Rroms.Web.Options;
using Rroms.Web.Services;

namespace Rroms.Web.Pages.Account;

/// <summary>
/// Second step of sign-in for users with two-step sign-in turned on. Owner: YY (YY-7).
/// Only reachable after a correct email and password: Identity then holds a short-lived
/// "half signed in" cookie. Without it, the page sends the user back to the login form.
/// </summary>

[EnableRateLimiting(AuthRateLimitOptions.PolicyName)]
public class LoginWithCodeModel : PageModel
{
    private readonly IAccountService _accountService;

    //Receives the account service from dependency injection
    public LoginWithCodeModel(IAccountService accountService)
    {
        _accountService = accountService;
    }

    //6 digit code from email
    [BindProperty]
    [Required(ErrorMessage = "Enter the 6-digit code from your email.")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "The code is 6 digits.")]
    [Display(Name = "6-digit code")]
    public string Code { get; set; } = string.Empty;

    //Page to open after signing in
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    //Keep me signed in choice
    [BindProperty(SupportsGet = true)]
    public bool RememberMe { get; set; }

    //Shows the code form, if a correct pw was just entered
    public async Task<IActionResult> OnGetAsync()
    {
        if (!await _accountService.HasPendingCodeSignInAsync())
        {
            return RedirectToPage("/Account/Login");
        }

        return Page();
    }

    //Checks the code and finishes signing in
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        ServiceResult<string> result = await _accountService.SignInWithCodeAsync(Code, RememberMe);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        // Same open-redirect protection as the login page.
        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl);
        }

        return LocalRedirect(result.Value ?? "/");
    }

    //Send a new code button
    public async Task<IActionResult> OnPostResendAsync()
    {
        if (!await _accountService.HasPendingCodeSignInAsync())
        {
            return RedirectToPage("/Account/Login");
        }

        await _accountService.ResendLoginCodeAsync();
        TempData["StatusMessage"] = "A new code has been sent. It can take a minute to arrive.";
        return RedirectToPage(new { ReturnUrl, RememberMe });
    }
}