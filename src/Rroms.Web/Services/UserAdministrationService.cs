using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Implements account administration with Identity's UserManager plus EF Core queries.
/// </summary>
public class UserAdministrationService : IUserAdministrationService
{
    /// <summary>The account list shows at most this many rows; use the search box to narrow it.</summary>
    public const int MaxResults = 200;

    /// <summary>Shown when a change would leave the restaurant with no active administrator.</summary>
    public const string LastAdministratorMessage = "There must always be at least one active administrator.";

    /// <summary>Shown when an administrator tries to demote or deactivate themselves.</summary>
    public const string SelfChangeMessage = "You cannot change your own role or deactivate your own account.";

    /// <summary>Shown when the requested account does not exist.</summary>
    public const string UserNotFoundMessage = "That account could not be found.";

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly INotificationService _notificationService;
    private readonly ILogger<UserAdministrationService> _logger;

    /// <summary>All dependencies are supplied by dependency injection.</summary>
    public UserAdministrationService(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        INotificationService notificationService,
        ILogger<UserAdministrationService> logger)
    {
        _context = context;
        _userManager = userManager;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserSummary>> SearchUsersAsync(string? searchText, string? roleName, bool? isActive)
    {
        IQueryable<ApplicationUser> users = _context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            string term = searchText.Trim();
            users = users.Where(user =>
                user.FullName.Contains(term)
                || (user.Email != null && user.Email.Contains(term))
                || (user.PhoneNumber != null && user.PhoneNumber.Contains(term)));
        }

        if (isActive.HasValue)
        {
            users = users.Where(user => user.IsActive == isActive.Value);
        }

        // Identity stores roles in a separate link table (AspNetUserRoles), so join users to their role name.
        // Left joins keep users who have no role at all, so an administrator can spot and fix them.
        IQueryable<UserSummary> query =
            from user in users
            join userRole in _context.UserRoles on user.Id equals userRole.UserId into userRoles
            from userRole in userRoles.DefaultIfEmpty()
            join role in _context.Roles on userRole.RoleId equals role.Id into roles
            from role in roles.DefaultIfEmpty()
            select new UserSummary
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? string.Empty,
                PhoneNumber = user.PhoneNumber,
                RoleName = role != null && role.Name != null ? role.Name : string.Empty,
                IsActive = user.IsActive,
                CreatedAtUtc = user.CreatedAtUtc
            };

        if (!string.IsNullOrWhiteSpace(roleName))
        {
            query = query.Where(summary => summary.RoleName == roleName);
        }

        return await query
            .OrderBy(summary => summary.FullName)
            .Take(MaxResults)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<UserSummary?> GetUserAsync(string userId)
    {
        ApplicationUser? user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return null;
        }

        string roleName = await GetSingleRoleAsync(user);

        return new UserSummary
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.PhoneNumber,
            RoleName = roleName,
            IsActive = user.IsActive,
            CreatedAtUtc = user.CreatedAtUtc
        };
    }

    /// <inheritdoc />
    public async Task<ServiceResult<string>> CreateUserAsync(
        string fullName, string email, string? phoneNumber, string roleName)
    {
        if (!RoleNames.All.Contains(roleName))
        {
            return ServiceResult<string>.Failure("Choose a valid role.");
        }

        string cleanEmail = email.Trim();
        var user = new ApplicationUser
        {
            UserName = cleanEmail,
            Email = cleanEmail,
            // Not confirmed yet: it becomes confirmed when the person proves they own the inbox
            // by setting their password with an emailed code (YY-7).
            EmailConfirmed = false,
            FullName = fullName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        // Creating the user and assigning the role are two database writes. Running them in one
        // transaction means we never end up with a user who has no role.
        // EnableRetryOnFailure is on, so a manual transaction must run inside the execution strategy.
        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();
        ServiceResult<string> result = await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

            // No password: the administrator never chooses or sees it (YY-7). Until the person sets one
            // with an emailed code, nobody can sign in to this account.
            IdentityResult createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                // Returning without CommitAsync rolls the transaction back when it is disposed.
                return ServiceResult<string>.Failure(createResult.Errors.Select(error => error.Description).ToArray());
            }

            IdentityResult roleResult = await _userManager.AddToRoleAsync(user, roleName);
            if (!roleResult.Succeeded)
            {
                return ServiceResult<string>.Failure(roleResult.Errors.Select(error => error.Description).ToArray());
            }

            await transaction.CommitAsync();
            _logger.LogInformation("Administrator created {Role} account {UserId}.", roleName, user.Id);
            return ServiceResult<string>.Success(user.Id);
        });

        if (result.Succeeded)
        {
            // Invitation (YY-7), sent only after the commit so nobody is invited to an account that was rolled back.
            // The link only pre-fills the email on the "Set or reset your password" page; the secret part is
            // the 6-digit code the person requests there, which goes to this inbox and expires in minutes.
            await _notificationService.NotifyUserAsync(
                user.Id,
                NotificationType.General,
                $"You have been invited to {SiteInfo.RestaurantName}",
                $"An administrator created a {roleName} account for you. Open the link, request a code and choose your own password.",
                $"/Account/ForgotPassword?email={Uri.EscapeDataString(cleanEmail)}");
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<ServiceResult> UpdateAccessAsync(
        string actingUserId, string userId, string roleName, bool isActive)
    {
        if (!RoleNames.All.Contains(roleName))
        {
            return ServiceResult.Failure("Choose a valid role.");
        }

        ApplicationUser? user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Failure(UserNotFoundMessage);
        }

        string currentRole = await GetSingleRoleAsync(user);
        bool roleChanging = currentRole != roleName;
        bool activeChanging = user.IsActive != isActive;

        // Rule 1: an administrator cannot lock themselves out by mistake.
        if (actingUserId == userId && (roleChanging || !isActive))
        {
            return ServiceResult.Failure(SelfChangeMessage);
        }

        // Rule 2: never remove the last active administrator.
        bool losingAdministrator = currentRole == RoleNames.Administrator
            && user.IsActive
            && (roleName != RoleNames.Administrator || !isActive);

        if (losingAdministrator)
        {
            IList<ApplicationUser> administrators = await _userManager.GetUsersInRoleAsync(RoleNames.Administrator);
            int otherActiveAdministrators = administrators.Count(admin => admin.IsActive && admin.Id != user.Id);
            if (otherActiveAdministrators == 0)
            {
                return ServiceResult.Failure(LastAdministratorMessage);
            }
        }

        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();
        ServiceResult result = await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

            // Name and phone are deliberately not touched: only the account owner changes them (YY-9).
            user.IsActive = isActive;

            IdentityResult updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return ServiceResult.Failure(updateResult.Errors.Select(error => error.Description).ToArray());
            }

            if (roleChanging)
            {
                // One role per user: remove every existing role, then add the new one.
                IList<string> existingRoles = await _userManager.GetRolesAsync(user);
                IdentityResult removeResult = await _userManager.RemoveFromRolesAsync(user, existingRoles);
                if (!removeResult.Succeeded)
                {
                    return ServiceResult.Failure(removeResult.Errors.Select(error => error.Description).ToArray());
                }

                IdentityResult addResult = await _userManager.AddToRoleAsync(user, roleName);
                if (!addResult.Succeeded)
                {
                    return ServiceResult.Failure(addResult.Errors.Select(error => error.Description).ToArray());
                }
            }

            if (roleChanging || activeChanging)
            {
                // The user's cookie still contains their old role. Changing the security stamp makes
                // Identity reject that cookie at its next check (every minute, set in Program.cs),
                // so a deactivated or demoted user is signed out everywhere.
                await _userManager.UpdateSecurityStampAsync(user);
            }

            await transaction.CommitAsync();
            _logger.LogInformation(
                "Administrator {ActingUserId} changed access for account {UserId} (role {Role}, active {IsActive}).",
                actingUserId, userId, roleName, isActive);
            return ServiceResult.Success();
        });

        if (result.Succeeded && activeChanging)
        {
            // Sent after the commit, so nobody is told about a change that was rolled back.
            // The email still reaches a suspended user, who can no longer open the inbox.
            await NotifyStatusChangeAsync(user.Id, isActive);
        }

        return result;
    }

    /// <summary>Tells the account owner that their account was suspended or reactivated (YY-9).</summary>
    private async Task NotifyStatusChangeAsync(string userId, bool isActive)
    {
        if (isActive)
        {
            await _notificationService.NotifyUserAsync(
                userId,
                NotificationType.General,
                "Your account has been reactivated",
                $"Your {SiteInfo.RestaurantName} account is active again. You can sign in as usual.",
                "/Account/Login");
            return;
        }

        await _notificationService.NotifyUserAsync(
            userId,
            NotificationType.General,
            "Your account has been suspended",
            $"An administrator has suspended your {SiteInfo.RestaurantName} account, so you can no longer sign in. "
                + "If you think this is a mistake, please contact the restaurant.");
    }

    /// <summary>Returns the user's role, or an empty string if they have none.</summary>
    private async Task<string> GetSingleRoleAsync(ApplicationUser user)
    {
        IList<string> roles = await _userManager.GetRolesAsync(user);
        return roles.FirstOrDefault() ?? string.Empty;
    }
}