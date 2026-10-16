using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Implements profile viewing, editing, dietary preferences (YY-9) and password changes with ASP.NET Core Identity.
/// </summary>
public class ProfileService : IProfileService
{
    //Shown if the account disappears between loading the page and saving it
    public const string AccountNotFoundMessage = "Your account could not be found. Please sign in again.";

    //Shown when tags are ticked but the consent box is not (YY-9)
    public const string ConsentRequiredMessage = "Tick the consent box to save dietary preferences, or untick all of them.";

    //Shown when the form posts a tag ID that does not exist (tampered or out-of-date form)
    public const string UnknownTagMessage = "One of the dietary options is not recognised. Please reload the page and try again.";

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<ProfileService> _logger;

    //All dependencies are supplied by dependency injection
    public ProfileService(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<ProfileService> logger)
    {
        _context = context;
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
            //The team rule is one role per user (enforced by YY-5), so the first role is the role.
            RoleName = roles.FirstOrDefault() ?? string.Empty,
            CreatedAtUtc = user.CreatedAtUtc,
            TwoStepEnabled = user.TwoFactorEnabled,
            HasDietaryConsent = user.DietaryConsentAtUtc != null,
            AvoidedTagIds = await GetAvoidedTagIdsAsync(userId)
        };
    }

    public async Task<ServiceResult> UpdateProfileAsync(string userId, string fullName, string? phoneNumber)
    {
        ApplicationUser? user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Failure(AccountNotFoundMessage);
        }

        user.FullName = fullName.Trim();
        user.PhoneNumber = CleanOptionalText(phoneNumber);

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

    public async Task<IReadOnlyList<DietaryTag>> GetDietaryTagsAsync()
    {
        return await _context.DietaryTags
            .AsNoTracking()
            .OrderBy(tag => tag.DisplayOrder)
            .ToListAsync();
    }

    public async Task<ServiceResult> UpdateDietaryPreferencesAsync(
        string userId, bool consentGiven, IReadOnlyCollection<int> avoidedTagIds)
    {
        ApplicationUser? user = await _context.Users
            .Include(u => u.AvoidedDietaryTags)
            .FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return ServiceResult.Failure(AccountNotFoundMessage);
        }

        // Rule 0: dietary preferences are a customer feature.
        bool isCustomer = await _userManager.IsInRoleAsync(user, RoleNames.Customer);
        if (!isCustomer)
        {
            return ServiceResult.Failure("Dietary preferences are only available to customers.");
        }

        // Rule 1: sensitive data needs explicit consent, so tags without consent are refused, not silently dropped.
        if (!consentGiven && avoidedTagIds.Count > 0)
        {
            return ServiceResult.Failure(ConsentRequiredMessage);
        }

        // Rule 2: every posted ID must be a real tag. Distinct() ignores duplicates from a tampered form.
        List<int> requestedIds = avoidedTagIds.Distinct().ToList();
        List<DietaryTag> tags = await _context.DietaryTags
            .Where(tag => requestedIds.Contains(tag.Id))
            .ToListAsync();
        if (tags.Count != requestedIds.Count)
        {
            return ServiceResult.Failure(UnknownTagMessage);
        }

        // Replace the whole set: EF Core adds and removes the join-table rows for us.
        user.AvoidedDietaryTags.Clear();
        foreach (DietaryTag tag in tags)
        {
            user.AvoidedDietaryTags.Add(tag);
        }

        // Withdrawing consent removes the data as well as the flag (PDPA: keep no more than needed).
        if (!consentGiven)
        {
            user.DietaryConsentAtUtc = null;
        }
        else if (user.DietaryConsentAtUtc == null)
        {
            // Keep the date consent was FIRST given; re-saving does not reset it.
            user.DietaryConsentAtUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("User {UserId} updated dietary preferences ({Count} tags).", userId, tags.Count);
        return ServiceResult.Success();
    }

    public async Task<IReadOnlyList<int>> GetAvoidedTagIdsAsync(string userId)
    {
        // Only customers who consented: no consent means the tags must not be used anywhere.
        return await _context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId && user.DietaryConsentAtUtc != null)
            .SelectMany(user => user.AvoidedDietaryTags)
            .Select(tag => tag.Id)
            .ToListAsync();
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

    //Turns blank input into null and trims the rest, so the database never stores "   "
    private static string? CleanOptionalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}