using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Let any signed in user edit their own profile and password. Owner YY-4
/// Every method takes the user ID from authentication cookie so user can only read and change their own account
/// </summary>

public interface IProfileService
{
    //Loads profile of given user, or null if no longer exist
    Task<ProfileDetails?> GetProfileAsync(string userId);

    //Updates the editable profile fields
    Task<ServiceResult> UpdateProfileAsync(string userId, string fullName, string? phoneNumber, string? diningPreferences);

    //Changes the password after checking current one
    Task<ServiceResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword);
}