using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Tests.Menu;

/// <summary>
/// A stand-in for YY's profile service, for the recommendation tests. Only the avoided tags are real: a test
/// says which tags a customer avoids, and everything else throws, so a test can never depend on it by accident.
/// Owner: YJ.
/// </summary>
public class FakeProfileService : IProfileService
{
    private readonly Dictionary<string, List<int>> _avoided = new Dictionary<string, List<int>>();

    /// <summary>Makes the given customer avoid the given dietary tags.</summary>
    /// <param name="userId">The customer.</param>
    /// <param name="tagIds">The tags they avoid.</param>
    public void SetAvoidedTags(string userId, params int[] tagIds)
    {
        _avoided[userId] = tagIds.ToList();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<int>> GetAvoidedTagIdsAsync(string userId)
    {
        List<int> ids = new List<int>();

        if (_avoided.TryGetValue(userId, out List<int>? stored))
        {
            ids = stored;
        }

        return Task.FromResult<IReadOnlyList<int>>(ids);
    }

    /// <inheritdoc />
    public Task<ProfileDetails?> GetProfileAsync(string userId)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<ServiceResult> UpdateProfileAsync(string userId, string fullName, string? phoneNumber)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DietaryTag>> GetDietaryTagsAsync()
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<ServiceResult> UpdateDietaryPreferencesAsync(string userId, bool consentGiven, IReadOnlyCollection<int> avoidedTagIds)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc />
    public Task<ServiceResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
    {
        throw new NotSupportedException();
    }
}
