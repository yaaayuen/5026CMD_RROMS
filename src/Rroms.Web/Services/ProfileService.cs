using Microsoft.AspNetCore.Identity;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Implements profile viewing, editing and password changes with ASP.NET Core Identity.
/// </summary>
public class ProfileService : IProfileService
{
    //Shown if the account disappears between loading the page and saving it
    public const string AccountNotFoundMessage = "Your account could not be found. Please sign in again.";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<ProfileService> _logger;

    //All dependencies are supplied by dependency injection
    public ProfileService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<ProfileService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    public async Task<ProfileDetails?> GetProfileAsync(string userId)
    {
        ApplicationUser? user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return null;
        }

        IList<string> roles = await _userManager.GetRolesAsync(user);

        return new ProfileDetails
        {
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            DiningPreferences = user.DiningPreferences,
            //The team rule is one role per user (enforced by YY-5), so the first role is the role.
            RoleName = roles.FirstOrDefault() ?? string.Empty,
            CreatedAtUtc = user.CreatedAtUtc,
            TwoStepEnabled = user.TwoFactorEnabled 
        };
    }

    public async Task<ServiceResult> UpdateProfileAsync(
        string userId, string fullName, string? phoneNumber, string? diningPreferences)
    {
        ApplicationUser? user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Failure(AccountNotFoundMessage);
        }

        user.FullName = fullName.Trim();
        user.PhoneNumber = CleanOptionalText(phoneNumber);

        //Dining preferences are a customer feature (used by staff and the recommendation engine).
        //Staff and administrators keep whatever value they had; the form does not show the field to them.
        bool isCustomer = await _userManager.IsInRoleAsync(user, RoleNames.Customer);
        if (isCustomer)
        {
            user.DiningPreferences = CleanOptionalText(diningPreferences);
        }

        //UpdateAsync also re-runs Identity's user validation (e.g. unique email) before saving.
        IdentityResult result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return ServiceResult.Failure(result.Errors.Select(error => error.Description).ToArray());
        }

        //The display name is not part of the cookie, so no re-sign-in is needed here.
        _logger.LogInformation("User {UserId} updated their profile.", userId);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
    {
        ApplicationUser? user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Failure(AccountNotFoundMessage);
        }

        //ChangePasswordAsync verifies the current password, checks the policy on the new one,
        //stores the new hash and updates the security stamp (which ends sessions on other devices).
        IdentityResult result = await _userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            return ServiceResult.Failure(result.Errors.Select(error => error.Description).ToArray());
        }

        //The security stamp changed, so re-issue THIS browser's cookie to keep the user signed in here.
        await _signInManager.RefreshSignInAsync(user);

        _logger.LogInformation("User {UserId} changed their password.", userId);
        return ServiceResult.Success();
    }

    //Turns blank input into null and trims the rest, so the database never stores
    private static string? CleanOptionalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}