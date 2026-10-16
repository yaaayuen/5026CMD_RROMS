using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Models;
using Rroms.Web.Services;

namespace Rroms.Tests.Accounts;

/// <summary>
/// YY-10 acceptance: dietary tags are stored only with consent, and withdrawing consent removes them.
/// </summary>
public class DietaryPreferenceTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";

    // Seeded tag IDs (DietaryTag.Seed): 2 = Dairy, 3 = Spicy.
    private static readonly int[] DairyAndSpicy = { 2, 3 };

    private readonly TestWebApplicationFactory _factory;

    public DietaryPreferenceTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<string> CreateCustomerAsync()
    {
        ApplicationUser user = await _factory.CreateUserAsync(
            $"diet-{Guid.NewGuid():N}@threegrills.test", Password, RoleNames.Customer);
        return user.Id;
    }

    [Fact]
    public async Task WithConsent_TagsAreSaved_AndAvailableToOtherModules()
    {
        string userId = await CreateCustomerAsync();

        using IServiceScope scope = _factory.Services.CreateScope();
        IProfileService profiles = scope.ServiceProvider.GetRequiredService<IProfileService>();

        ServiceResult result = await profiles.UpdateDietaryPreferencesAsync(userId, true, DairyAndSpicy);
        IReadOnlyList<int> avoided = await profiles.GetAvoidedTagIdsAsync(userId);

        Assert.True(result.Succeeded);
        Assert.Equal(DairyAndSpicy.OrderBy(id => id), avoided.OrderBy(id => id));
    }

    [Fact]
    public async Task WithoutConsent_TagsAreRefused_AndWithdrawingConsentDeletesThem()
    {
        string userId = await CreateCustomerAsync();

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IProfileService profiles = scope.ServiceProvider.GetRequiredService<IProfileService>();

            // Tags ticked without consent are refused.
            ServiceResult refused = await profiles.UpdateDietaryPreferencesAsync(userId, false, DairyAndSpicy);
            Assert.False(refused.Succeeded);
            Assert.Contains(ProfileService.ConsentRequiredMessage, refused.Errors);

            // Save with consent, then withdraw it.
            Assert.True((await profiles.UpdateDietaryPreferencesAsync(userId, true, DairyAndSpicy)).Succeeded);
            Assert.True((await profiles.UpdateDietaryPreferencesAsync(userId, false, Array.Empty<int>())).Succeeded);
        }

        // A fresh scope reads straight from the database, not from a cached DbContext.
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IProfileService profiles = scope.ServiceProvider.GetRequiredService<IProfileService>();
            Assert.Empty(await profiles.GetAvoidedTagIdsAsync(userId));
        }
    }
}
