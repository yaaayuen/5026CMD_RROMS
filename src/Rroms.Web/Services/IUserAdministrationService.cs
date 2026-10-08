using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Administrator management of customer and staff accounts. Owner: YY (YY-5).
/// Rules enforced here (not in the pages), so they cannot be bypassed:
/// one role per user; there must always be at least one active administrator;
/// administrators cannot change their own role or deactivate themselves.
/// </summary>

public interface IUserAdministrationService
{
    //Find accounts matching the filters, ordered by name
    Task<IReadOnlyList<UserSummary>> SearchUsersAsync(string? searchText, string? roleName, bool? isActive);

    //Loads one account, or null if it does not exist
    Task<UserSummary?> GetUserAsync(string userId);

    //Creates an account with the given role (used to create staff account)
    Task<ServiceResult<string>> CreateUserAsync(string fullName, string email, string? phoneNumber, string password, string roleName);

    //Updates name, phone, role, and active status
    Task<ServiceResult> UpdateUserAsync(string actingUserId, string userId, string fullName, string? phoneNumber, string roleName, bool isActive);

    //Sets a new password for a user who has forgotten theirs
    Task<ServiceResult> ResetPasswordAsync(string userId, string newPassword);
}
