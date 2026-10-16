using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Models;
using Rroms.Web.Services;

namespace Rroms.Tests.Admin;

/// <summary>
/// YY-5 / YY-9 acceptance: a suspended account cannot sign in, its owner is emailed,
/// and the "suspended" message is only shown to someone who knows the password.
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

    /// <summary>Creates a customer and suspends it through the real service, as the seeded admin.</summary>
    private async Task<string> CreateSuspendedCustomerAsync()
    {
        string email = UniqueEmail();
        ApplicationUser user = await _factory.CreateUserAsync(email, Password, RoleNames.Customer);
        string adminId = await GetSeededAdminIdAsync();

        using IServiceScope scope = _factory.Services.CreateScope();
        IUserAdministrationService service = scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
        ServiceResult result = await service.UpdateAccessAsync(adminId, user.Id, RoleNames.Customer, false);
        Assert.True(result.Succeeded);

        return email;
    }

    [Fact]
    public async Task SuspendedUser_IsEmailed_AndSeesSuspendedMessage_WithCorrectPassword()
    {
        string email = await CreateSuspendedCustomerAsync();

        HttpClient client = _factory.CreateSecureClient();
        HttpResponseMessage response = await FormHelper.LoginAsync(client, email, Password);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Contains(AccountService.SuspendedMessage, html);
        Assert.Contains(_factory.EmailQueue.Queued, message => message.ToAddress == email && message.Subject.Contains("suspended"));
    }

    [Fact]
    public async Task SuspendedUser_WithWrongPassword_GetsGenericMessage()
    {
        string email = await CreateSuspendedCustomerAsync();

        HttpClient client = _factory.CreateSecureClient();
        HttpResponseMessage response = await FormHelper.LoginAsync(client, email, "Wrong!Password1");
        string html = await response.Content.ReadAsStringAsync();

        // Without the right password nobody learns that the account exists or is suspended.
        Assert.Contains(AccountService.InvalidLoginMessage, html);
        Assert.DoesNotContain(AccountService.SuspendedMessage, html);
    }
}
