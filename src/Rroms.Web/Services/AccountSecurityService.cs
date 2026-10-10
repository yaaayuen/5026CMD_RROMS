using System.Net;
using Microsoft.AspNetCore.Identity;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Models;
using Rroms.Web.Services.Email;

namespace Rroms.Web.Services;

/// <summary>
/// Implements email codes with ASP.NET Core Identity's built-in email token provider. Owner: YY (YY-7).
/// Identity generates the 6-digit codes (TokenOptions.DefaultEmailProvider). Each code is tied to a
/// purpose and to the user's security stamp, so a password code cannot be used to sign in, and a code
/// stops working once the password changes. Codes expire after a few minutes.
/// </summary>

public class AccountSecurityService : IAccountSecurityService
{
    //Shown for any wrong, expired or already-used code, and for unknown accounts
    public const string InvalidCodeMessage = "That code is invalid or has expired. Request a new code and try again.";

    //Purpose for password codes. Purposes keep codes for different actions separate
    private const string SetPasswordPurpose = "SetPassword";

    //Purpose for the code that turns two-step sign-in on
    private const string EnableTwoStepPurpose = "EnableTwoStep";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IEmailQueue _emailQueue;
    private readonly INotificationService _notificationService;
    private readonly ILogger<AccountSecurityService> _logger;

    //All dependencies are supplied by dependency injection.
    public AccountSecurityService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IEmailQueue emailQueue,
        INotificationService notificationService,
        ILogger<AccountSecurityService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _emailQueue = emailQueue;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task RequestPasswordCodeAsync(string email)
    {
        ApplicationUser? user = await _userManager.FindByEmailAsync(email.Trim());

        // Unknown or deactivated accounts get no email, but the caller cannot tell the difference.
        if (user == null || !user.IsActive || user.Email == null)
        {
            _logger.LogInformation("Password code requested for an unknown or inactive account.");
            return;
        }

        string code = await _userManager.GenerateUserTokenAsync(user, TokenOptions.DefaultEmailProvider, SetPasswordPurpose);
        SendCodeEmail(user, "to choose a new password", code);
        _logger.LogInformation("Password code emailed to user {UserId}.", user.Id);
    }

    public async Task<ServiceResult> ResetPasswordWithCodeAsync(string email, string code, string newPassword)
    {
        ApplicationUser? user = await _userManager.FindByEmailAsync(email.Trim());
        if (user == null || !user.IsActive)
        {
            return ServiceResult.Failure(InvalidCodeMessage);
        }

        bool codeIsValid = await VerifyCodeAsync(user, SetPasswordPurpose, code);
        if (!codeIsValid)
        {
            return ServiceResult.Failure(InvalidCodeMessage);
        }

        // The code proved the user owns the inbox. Identity's own reset token is then created and used
        // immediately, so the normal checks still run (password policy, new security stamp).
        string resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        IdentityResult result = await _userManager.ResetPasswordAsync(user, resetToken, newPassword);
        if (!result.Succeeded)
        {
            // The password broke the policy. The stamp has not changed, so the same code can be tried again.
            return ServiceResult.Failure(result.Errors.Select(error => error.Description).ToArray());
        }

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
        }

        // Security notice: a password change the owner did not make should never go unnoticed.
        await _notificationService.NotifyUserAsync(
            user.Id,
            NotificationType.General,
            "Your password was changed",
            "Your password was just changed using a code sent to your email. If this was not you, contact the restaurant straight away.");

        _logger.LogInformation("User {UserId} set a new password with an email code.", user.Id);
        return ServiceResult.Success();
    }

    public async Task SendLoginCodeAsync(ApplicationUser user)
    {
        // Identity's two-factor code for the email provider; checked later by SignInManager.TwoFactorSignInAsync.
        string code = await _userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultEmailProvider);
        SendCodeEmail(user, "to finish signing in", code);
    }

    public async Task<bool> IsTwoStepEnabledAsync(string userId)
    {
        ApplicationUser? user = await _userManager.FindByIdAsync(userId);
        return user != null && user.TwoFactorEnabled;
    }

    public async Task<ServiceResult> SendTwoStepSetupCodeAsync(string userId)
    {
        ApplicationUser? user = await _userManager.FindByIdAsync(userId);
        if (user == null || user.Email == null)
        {
            return ServiceResult.Failure("Your account could not be found. Please sign in again.");
        }

        string code = await _userManager.GenerateUserTokenAsync(user, TokenOptions.DefaultEmailProvider, EnableTwoStepPurpose);
        SendCodeEmail(user, "to turn on two-step sign-in", code);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> EnableTwoStepAsync(string userId, string code)
    {
        ApplicationUser? user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Failure(InvalidCodeMessage);
        }

        bool codeIsValid = await VerifyCodeAsync(user, EnableTwoStepPurpose, code);
        if (!codeIsValid)
        {
            return ServiceResult.Failure(InvalidCodeMessage);
        }

        // Identity only sends sign-in codes to confirmed addresses; the code we just checked proves this one.
        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);
        await _userManager.SetTwoFactorEnabledAsync(user, true);

        // Changing the setting changes the security stamp, which would sign this browser out at the next
        // stamp check (every minute). Re-issuing the cookie keeps the user signed in here.
        await _signInManager.RefreshSignInAsync(user);

        await _notificationService.NotifyUserAsync(
            user.Id,
            NotificationType.General,
            "Two-step sign-in turned on",
            "From now on, signing in also needs a 6-digit code sent to your email.");

        _logger.LogInformation("User {UserId} turned on two-step sign-in.", user.Id);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> DisableTwoStepAsync(string userId, string currentPassword)
    {
        ApplicationUser? user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Failure("Your account could not be found. Please sign in again.");
        }

        // Asking for the password stops someone at an unlocked computer from weakening the account.
        bool passwordIsCorrect = await _userManager.CheckPasswordAsync(user, currentPassword);
        if (!passwordIsCorrect)
        {
            return ServiceResult.Failure("Your current password is incorrect.");
        }

        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _signInManager.RefreshSignInAsync(user);

        await _notificationService.NotifyUserAsync(
            user.Id,
            NotificationType.General,
            "Two-step sign-in turned off",
            "Two-step sign-in was turned off for your account. If this was not you, change your password and contact the restaurant.");

        _logger.LogInformation("User {UserId} turned off two-step sign-in.", user.Id);
        return ServiceResult.Success();
    }

    //Checks a code and counts wrong guesses towards the account lockout
    private async Task<bool> VerifyCodeAsync(ApplicationUser user, string purpose, string code)
    {
        if (await _userManager.IsLockedOutAsync(user))
        {
            return false;
        }

        bool isValid = await _userManager.VerifyUserTokenAsync(user, TokenOptions.DefaultEmailProvider, purpose, code.Trim());
        if (!isValid)
        {
            await _userManager.AccessFailedAsync(user);
            return false;
        }

        await _userManager.ResetAccessFailedCountAsync(user);
        return true;
    }

    //Queues an email containing a code, code only goes to the owner inbox
    private void SendCodeEmail(ApplicationUser user, string reason, string code)
    {
        if (user.Email == null)
        {
            return;
        }

        string body =
            "<div style=\"font-family:Arial,sans-serif;color:#2b2d42;max-width:560px;\">" +
            $"<p>Hi {WebUtility.HtmlEncode(user.FullName)},</p>" +
            $"<p>Use this code {WebUtility.HtmlEncode(reason)}:</p>" +
            $"<p style=\"font-size:28px;font-weight:bold;letter-spacing:6px;color:#d81159;\">{WebUtility.HtmlEncode(code)}</p>" +
            "<p>It expires in a few minutes. Never share it: our staff will never ask for it.</p>" +
            "<p>If you did not ask for this code, you can ignore this email.</p>" +
            $"<p style=\"color:#5c5f77;font-size:12px;\">{WebUtility.HtmlEncode(SiteInfo.RestaurantName)}</p>" +
            "</div>";

        _emailQueue.TryEnqueue(new EmailMessage(user.Email, $"[{SiteInfo.RestaurantName}] Your verification code", body));
    }

}
