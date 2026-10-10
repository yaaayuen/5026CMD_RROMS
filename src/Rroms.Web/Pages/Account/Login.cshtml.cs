using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Rroms.Web.Common;
using Rroms.Web.Options;
using Rroms.Web.Services;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Pages.Account;

/// <summary>
/// Sign in page for all three roles. Owner: YY (YY-2)
/// Rate limited: one IP address can only post this form a limited number of time per min
/// </summary>

[EnableRateLimiting(AuthRateLimitOptions.PolicyName)]
public class LoginModel : PageModel
{
    private readonly IAccountService _accountService;

    //Receives the account service from dependency injection
    public LoginModel(IAccountService accountService)
    {
        _accountService = accountService;
    }

    //The data submitted by the user on the login form
    [BindProperty]
    public LoginInput Input { get; set; } = new LoginInput();

    // The page the user tried to open before being sent to log in, e.g. "/Customer".
    //SupportsGet lets it be read from the query string on the first visit.
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    //Shows the form, or send signed-in users to their home page
    public IActionResult OnGet()
    {
        if(User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Index");
        }

        return Page();
    }

    //Validates form and signs user in
    public async Task<IActionResult> OnPostAsync()
    {
        // Server-side validation always runs, even if the browser already validated the form.
        if (!ModelState.IsValid)
        {
            return Page();
        }

        ServiceResult<SignInOutcome> result = await _accountService.SignInAsync(Input.Email, Input.Password, Input.RememberMe);
        if (!result.Succeeded || result.Value == null)
        {
            foreach (string error in result.Errors)
            {
                // An empty key shows the message in the form-level validation summary.
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        if (result.Value.RequiresCode)
        {
            // Two-step sign-in (YY-7): a code has been emailed. Pass ReturnUrl and RememberMe along.
            return RedirectToPage("/Account/LoginWithCode", new { ReturnUrl, Input.RememberMe });
        }

        // Only follow ReturnUrl if it points inside this site. Otherwise an attacker could send a link
        // like /Account/Login?ReturnUrl=https://evil.example and use our login to redirect victims (open redirect).
        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl);
        }

        return LocalRedirect(result.Value.HomePath);
    }

    //Form fields for signing in. Data annotations drive both server and browser validation.
    public class LoginInput
    {
        //Login email
        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        //Password; never logged or stored in plain text
        [Required(ErrorMessage = "Enter your password.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        //Keep the user signed in after the browser is closed
        [Display(Name = "Keep me signed in")]
        public bool RememberMe { get; set; }
    }
}