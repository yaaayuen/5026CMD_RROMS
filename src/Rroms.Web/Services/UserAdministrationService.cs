using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Contracts;

namespace Rroms.Web.Services;

/// <summary>
/// Implements account administration
/// </summary>

public class UserAdministrationService : IUserAdministrationService
{
    //The account list showd at most this many rows; search function to narrow
    public const int MaxResults = 200;

    //Shown when an action/changes will delete all admin accounts
    public const string LastAdministratorMessage = "There must always be at least one active administrator.";

    //Shown when admin try to change their own role or deactivate themselves
    public const string SelfChangeMessage = "You cannot change your own role or deactivate your own account.";

    //Shown then the requested account doesnt exist
    public const string UserNotFoundMessage = "That account could not be found.";

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly INotificationService _notificationService;
    private readonly ILogger<UserAdministrationService> _logger;

    //All depencies are supplied by dependency injection
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

        //Identity stores roles in a separate link table (AspNetUserRoles), so join users to their role name.
        //Left joins keep users who have no role at all, so an administrator can spot and fix them.
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

    public async Task<ServiceResult<string>> CreateUserAsync(
        string fullName, string email, string? phoneNumber, string password, string roleName)
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
            // An administrator is vouching for this address, so it is treated as confirmed.
            EmailConfirmed = true,
            FullName = fullName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        //Creating the user and assigning the role are two database writes. Running them in one
        //transaction means we never end up with a user who has no role.
        //EnableRetryOnFailure is on, so a manual transaction must run inside the execution strategy.
        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

            IdentityResult createResult = await _userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                //Returning without CommitAsync rolls the transaction back when it is disposed.
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
    }

    public async Task<ServiceResult> UpdateUserAsync(
        string actingUserId, string userId, string fullName, string? phoneNumber, string roleName, bool isActive)
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

        //Rule 1: an administrator cannot lock themselves out by mistake.
        if (actingUserId == userId && (roleChanging || !isActive))
        {
            return ServiceResult.Failure(SelfChangeMessage);
        }

        //Rule 2: never remove the last active administrator.
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
        return await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction = await _context.Database.BeginTransactionAsync();

            user.FullName = fullName.Trim();
            user.PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
            user.IsActive = isActive;

            IdentityResult updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return ServiceResult.Failure(updateResult.Errors.Select(error => error.Description).ToArray());
            }

            if (roleChanging)
            {
                //One role per user: remove every existing role, then add the new one.
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
                //The user's cookie still contains their old role. Changing the security stamp makes
                //Identity reject that cookie at its next check (every minute, set in Program.cs),
                //so a deactivated or demoted user is signed out everywhere.
                await _userManager.UpdateSecurityStampAsync(user);
            }

            await transaction.CommitAsync();
            _logger.LogInformation(
                "Administrator {ActingUserId} updated account {UserId} (role {Role}, active {IsActive}).",
                actingUserId, userId, roleName, isActive);
            return ServiceResult.Success();
        });
    }

    public async Task<ServiceResult> ResetPasswordAsync(string userId, string newPassword)
    {
        ApplicationUser? user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ServiceResult.Failure(UserNotFoundMessage);
        }

        //Identity only resets a password with a one-time token, even for administrators.
        //Generating and immediately using the token keeps Identity's normal checks (policy, security stamp).
        string resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        IdentityResult result = await _userManager.ResetPasswordAsync(user, resetToken, newPassword);
        if (!result.Succeeded)
        {
            return ServiceResult.Failure(result.Errors.Select(error => error.Description).ToArray());
        }

        //Clear any lockout so the user can sign in with the new password straight away.
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);

        // Security notice (YY-6): an unexpected reset should never go unnoticed by the account owner.
        await _notificationService.NotifyUserAsync(
            user.Id,
            NotificationType.General,
            "Your password was reset",
            "An administrator has reset your password. If you did not ask for this, please contact the restaurant.");

        _logger.LogInformation("Password reset by administrator for account {UserId}.", userId);
        return ServiceResult.Success();
    }

    //Returns the user's role, or an empty string if they have none.
    private async Task<string> GetSingleRoleAsync(ApplicationUser user)
    {
        IList<string> roles = await _userManager.GetRolesAsync(user);
        return roles.FirstOrDefault() ?? string.Empty;
    }
}
