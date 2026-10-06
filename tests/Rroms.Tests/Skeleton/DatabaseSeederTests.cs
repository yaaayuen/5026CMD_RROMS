using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Models;

namespace Rroms.Tests.Skeleton;

/// <summary>
/// YY-1 acceptance: the bootstrap administrator is created at start-up
/// and its password is stored as a hash, never as plain text.
/// </summary>
public class DatabaseSeederTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public DatabaseSeederTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        // Creating a client starts the app, which runs DatabaseInitializer (schema + seed).
        _factory.CreateClient();
    }

    [Fact]
    public async Task BootstrapAdministrator_PasswordIsStoredAsHash_NotPlainText()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        ApplicationUser? admin = await userManager.FindByEmailAsync(TestWebApplicationFactory.AdminEmail);

        Assert.NotNull(admin);
        Assert.NotEqual(TestWebApplicationFactory.AdminPassword, admin.PasswordHash);
        Assert.True(await userManager.CheckPasswordAsync(admin, TestWebApplicationFactory.AdminPassword));
    }
}