using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Models;

namespace Rroms.Tests.Accounts;

/// <summary>
/// YY-4 acceptance: users can change only the fields on their own profile form (over-posting is ignored).
/// </summary>
public class ProfileTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";

    private readonly TestWebApplicationFactory _factory;

    public ProfileTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string UniqueEmail()
    {
        return $"profile-{Guid.NewGuid():N}@threegrills.test";
    }

    private async Task<ApplicationUser?> ReloadUserAsync(string userId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await userManager.FindByIdAsync(userId);
    }

    [Fact]
    public async Task OverPosting_ExtraFields_AreIgnored()
    {
        string email = UniqueEmail();
        ApplicationUser user = await _factory.CreateUserAsync(email, Password, RoleNames.Customer);
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);

        // An attacker adds fields that are not on the form, hoping the server binds them.
        var fields = new Dictionary<string, string>
        {
            ["Input.FullName"] = "Still Me",
            ["Input.Email"] = "hijack@evil.example",
            ["Input.IsActive"] = "false",
            ["Email"] = "hijack@evil.example",
            ["IsActive"] = "false"
        };
        await FormHelper.PostFormAsync(client, "/Account/Profile", "/Account/Profile", fields);

        ApplicationUser? saved = await ReloadUserAsync(user.Id);
        Assert.NotNull(saved);
        Assert.Equal("Still Me", saved.FullName);
        Assert.Equal(email, saved.Email);
        Assert.True(saved.IsActive);
    }
}