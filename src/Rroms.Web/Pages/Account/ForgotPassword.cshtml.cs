using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Rroms.Web.Options;
using Rroms.Web.Services;

namespace Rroms.Web.Pages.Account;

/// <summary>
/// Step 1 of setting a password: ask for a 6-digit code by email. Owner: YY (YY-7).
/// Used by users who forgot their password and by newly invited staff.
/// </summary>

[EnableRateLimiting(AuthRateLimitOptions.PolicyName)]
public class ForgotPasswordModel : PageModel
{
    private readonly IAccountSecurityService _accountSecurity;

    //Receives the account security service from dependency injection
    public ForgotPasswordModel(IAccountSecurityService accountSecurity)
    {
        _accountSecurity = accountSecurity;
    }

    //Email typed into the form
    [BindProperty]
    public ForgotPasswordInput Input { get; set; } = new ForgotPasswordInput();

    //Shows the form, pre-filling the email from an invitation link
    public void OnGet(string? email)
    {
        Input.Email = email ?? string.Empty;
    }

    //Requests a code, then moves on to the "enter your code" page
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        await _accountSecurity.RequestPasswordCodeAsync(Input.Email);

        // Same message for every email address, registered or not.
        TempData["StatusMessage"] = "If that email has an account, a 6-digit code is on its way. Check your inbox (and spam folder).";
        return RedirectToPage("/Account/ResetPassword", new { email = Input.Email.Trim() });
    }

    //Form field for requesting a code
    public class ForgotPasswordInput
    {
        //The account's email address
        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;
    }
}