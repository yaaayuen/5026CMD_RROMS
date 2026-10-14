using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Models;
using Rroms.Web.Services;

namespace Rroms.Tests.Admin;

/// <summary>
/// YY-5 acceptance: an account deactivated by an administrator can no longer sign in.
/// </summary>
public class UserAdministrationTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";

    private readonly TestWebApplicationFactory _factory;

    public UserAdministrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string UniqueEmail()
    {
        return $"admin-test-{Guid.NewGuid():N}@threegrills.test";
    }

    private async Task<string> GetSeededAdminIdAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser? admin = await userManager.FindByEmailAsync(TestWebApplicationFactory.AdminEmail);
        return admin!.Id;
    }

    [Fact]
    public async Task DeactivatedUser_CannotSignIn()
    {
        string email = UniqueEmail();
        ApplicationUser user = await _factory.CreateUserAsync(email, Password, RoleNames.Customer);
        string adminId = await GetSeededAdminIdAsync();

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IUserAdministrationService service = scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
            ServiceResult result = await service.UpdateUserAsync(adminId, user.Id, "Test User", null, RoleNames.Customer, false);
            Assert.True(result.Succeeded);
        }

        HttpClient client = _factory.CreateSecureClient();
        HttpResponseMessage response = await FormHelper.LoginAsync(client, email, Password);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Contains(AccountService.InvalidLoginMessage, html);
    }
}
