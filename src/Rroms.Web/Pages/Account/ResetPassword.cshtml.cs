using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Rroms.Web.Common;
using Rroms.Web.Options;
using Rroms.Web.Services;

namespace Rroms.Web.Pages.Account;

/// <summary>
/// Step 2 of setting a password: enter the emailed code and a new password. Owner: YY (YY-7).
/// Wrong codes count towards the account lockout, and the page is rate limited, so the one million possible codes cannot be guessed.
/// </summary>

[EnableRateLimiting(AuthRateLimitOptions.PolicyName)]
public class ResetPasswordModel : PageModel
{
    private readonly IAccountSecurityService _accountSecurity;

    //Receives the account security service from dependency injection
    public ResetPasswordModel(IAccountSecurityService accountSecurity)
    {
        _accountSecurity = accountSecurity;
    }

    //The values typed into the form
    [BindProperty]
    public ResetPasswordInput Input { get; set; } = new ResetPasswordInput();

    //Shows the form with the email filled in from the previous step
    public void OnGet(string? email)
    {
        Input.Email = email ?? string.Empty;
    }

    //Checks the code and saves the new password
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        ServiceResult result = await _accountSecurity.ResetPasswordWithCodeAsync(Input.Email, Input.Code, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        TempData["StatusMessage"] = "Your password has been saved. You can now sign in with it.";
        return RedirectToPage("/Account/Login");
    }

    //Form fields for choosing a new password
    public class ResetPasswordInput
    {
        //The account's email address
        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        //The 6-digit code from the email
        [Required(ErrorMessage = "Enter the 6-digit code from your email.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "The code is 6 digits.")]
        [Display(Name = "6-digit code")]
        public string Code { get; set; } = string.Empty;

        //The new password; Identity checks the full policy
        [Required(ErrorMessage = "Enter a new password.")]
        [StringLength(100, MinimumLength = 10, ErrorMessage = "Password must be at least 10 characters.")]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        //Must match the new password, to catch typing mistakes
        [Required(ErrorMessage = "Type the new password again.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
        [Display(Name = "Confirm new password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
