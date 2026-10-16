using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Let any signed in user edit their own profile and password. Owner YY-4 (dietary tags YY-9)
/// Every method takes the user ID from authentication cookie so user can only read and change their own account
/// </summary>

public interface IProfileService
{
    //Loads profile of given user, or null if no longer exist
    Task<ProfileDetails?> GetProfileAsync(string userId);

    //Updates the editable profile fields (name and phone)
    Task<ServiceResult> UpdateProfileAsync(string userId, string fullName, string? phoneNumber);

    //All dietary tags in display order, for the profile checkboxes
    Task<IReadOnlyList<DietaryTag>> GetDietaryTagsAsync();

    //Saves the tags a customer avoids. Without consent nothing is stored and any existing tags are removed.
    Task<ServiceResult> UpdateDietaryPreferencesAsync(string userId, bool consentGiven, IReadOnlyCollection<int> avoidedTagIds);

    //IDs of the tags a customer avoids, for other modules such as YJ's recommender.
    //Returns an empty list if the customer has not consented or does not exist.
    Task<IReadOnlyList<int>> GetAvoidedTagIdsAsync(string userId);

    //Changes the password after checking current one
    Task<ServiceResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword);
}