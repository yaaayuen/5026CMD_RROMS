using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Rroms.Web.Common;
using Rroms.Web.Options;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Pages.Account;

/// <summary>
///  Lets any signed-in user turn email two-step sign-in on or off. Owner: YY (YY-7).
/// Protected by AuthorizePage("/Account/TwoStep") in Program.cs
/// Optional for everyone in YY-7. Making it compulsory for staff and administrators is YY-8.
/// The user ID always comes from the cookie, never from the form.
/// Each button posts to its own handler (OnPostSendCode, OnPostEnable, OnPostDisable),
/// so each handler only checks the field it needs.
/// </summary>

[EnableRateLimiting(AuthRateLimitOptions.PolicyName)]
public class TwoStepModel : PageModel
{
    private const string CodeSentKey = "TwoStepCodeSent";
    private readonly IAccountSecurityService _accountSecurity;

    //Receives the account security service from dependency injection
    public TwoStepModel(IAccountSecurityService accountSecurity)
    {
        _accountSecurity = accountSecurity;
    }

    //The 6-digit code (turning on)
    [BindProperty]
    [Display(Name = "6-digit code")]
    public string? Code { get; set; }

    //The current password (turning off)
    [BindProperty]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string? CurrentPassword { get; set; }

    //True when two-step sign-in is on for this user
    public bool IsEnabled { get; private set; }

    //True straight after a setup code was emailed, to show the code box
    public bool CodeSent { get; private set; }

    //Shows the current status
    public async Task OnGetAsync()
    {
        IsEnabled = await _accountSecurity.IsTwoStepEnabledAsync(User.GetUserId());
        CodeSent = TempData[CodeSentKey] is true;
    }

    //Emails a setup code
    public async Task<IActionResult> OnPostSendCodeAsync()
    {
        ServiceResult result = await _accountSecurity.SendTwoStepSetupCodeAsync(User.GetUserId());
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = result.Errors[0];
            return RedirectToPage();
        }

        TempData[CodeSentKey] = true;
        TempData["StatusMessage"] = "We've emailed you a 6-digit code. Enter it below.";
        return RedirectToPage();
    }

    //Checks the code and turns two step sign in on
    public async Task<IActionResult> OnPostEnableAsync()
    {
        string code = (Code ?? string.Empty).Trim();
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            ModelState.AddModelError(nameof(Code), "Enter the 6-digit code from your email.");
            return await RedisplayAsync(codeSent: true);
        }

        ServiceResult result = await _accountSecurity.EnableTwoStepAsync(User.GetUserId(), code);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Errors[0]);
            return await RedisplayAsync(codeSent: true);
        }

        TempData["StatusMessage"] = "Two-step sign-in is on. Next time you sign in, we'll email you a code.";
        return RedirectToPage();
    }

    //Checks the password and turns two step sign in off
    public async Task<IActionResult> OnPostDisableAsync()
    {
        if (string.IsNullOrEmpty(CurrentPassword))
        {
            ModelState.AddModelError(nameof(CurrentPassword), "Enter your current password.");
            return await RedisplayAsync(codeSent: false);
        }

        ServiceResult result = await _accountSecurity.DisableTwoStepAsync(User.GetUserId(), CurrentPassword);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Errors[0]);
            return await RedisplayAsync(codeSent: false);
        }

        TempData["StatusMessage"] = "Two-step sign-in is off.";
        return RedirectToPage();
    }

    //shows the page again with errors, but no lose the step user doing
    private async Task<IActionResult> RedisplayAsync(bool codeSent)
    {
        IsEnabled = await _accountSecurity.IsTwoStepEnabledAsync(User.GetUserId());
        CodeSent = codeSent;
        return Page();
    }
}
