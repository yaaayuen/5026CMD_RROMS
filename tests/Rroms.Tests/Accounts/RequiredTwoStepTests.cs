using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Models;
using Rroms.Web.Services;
using Rroms.Web.Services.Email;

namespace Rroms.Tests.Accounts;

/// <summary>
/// YY-9 acceptance: staff and administrators must use two-step sign-in; customers are not affected.
/// </summary>
public class RequiredTwoStepTests : IClassFixture<RequiredTwoStepTests.TwoStepOnFactory>
{
    private const string GoodPassword = "Grill!Test2026";

    //The code sits alone inside an HTML element in the email
    private static readonly Regex CodePattern = new Regex(@">(\d{6})<");

    private readonly TwoStepOnFactory _factory;

    public RequiredTwoStepTests(TwoStepOnFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The normal test factory with the YY-9 rule switched back on.</summary>
    public class TwoStepOnFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            // Added after the base settings, so this value wins.
            builder.ConfigureAppConfiguration((context, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TwoStep:RequiredForStaff"] = "true"
                }));
        }
    }

    //Gives each test its own account so tests never interfere with each other
    private static string UniqueEmail()
    {
        return $"twostep-{Guid.NewGuid():N}@threegrills.test";
    }

    //Reads the newest code emailed to an address, like the user opening their inbox
    private string LatestCodeFor(string email)
    {
        EmailMessage message = _factory.EmailQueue.Queued
            .Last(queued => queued.ToAddress == email && queued.Subject.Contains("verification code"));
        return CodePattern.Match(message.HtmlBody).Groups[1].Value;
    }

    [Theory]
    [InlineData(RoleNames.Staff, "/Staff")]
    [InlineData(RoleNames.Administrator, "/Admin")]
    public async Task StaffAndAdmin_WithoutTwoStep_MustEnterTheEmailedCode(string roleName, string homePath)
    {
        string email = UniqueEmail();
        await _factory.CreateUserAsync(email, GoodPassword, roleName);
        HttpClient client = _factory.CreateSecureClient();

        // Correct password: sent to the code page, not to the dashboard.
        HttpResponseMessage login = await FormHelper.LoginAsync(client, email, GoodPassword);
        Assert.StartsWith("/Account/LoginWithCode", login.Headers.Location?.OriginalString);

        // Not signed in yet: the protected area sends the browser back to the login page.
        HttpResponseMessage beforeCode = await client.GetAsync(homePath);
        Assert.Contains("/Account/Login", beforeCode.Headers.Location?.OriginalString);

        // The emailed code finishes signing in.
        HttpResponseMessage withCode = await FormHelper.PostFormAsync(client, "/Account/LoginWithCode", "/Account/LoginWithCode",
            new Dictionary<string, string> { ["Code"] = LatestCodeFor(email) });
        Assert.Equal(homePath, withCode.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Customer_StillSignsInWithPasswordOnly()
    {
        string email = UniqueEmail();
        await _factory.CreateUserAsync(email, GoodPassword, RoleNames.Customer);

        HttpResponseMessage login = await FormHelper.LoginAsync(_factory.CreateSecureClient(), email, GoodPassword);

        Assert.Equal("/Customer", login.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Staff_CannotTurnTwoStepOff()
    {
        string email = UniqueEmail();
        ApplicationUser user = await _factory.CreateUserAsync(email, GoodPassword, RoleNames.Staff);
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, GoodPassword);
        await FormHelper.PostFormAsync(client, "/Account/LoginWithCode", "/Account/LoginWithCode",
            new Dictionary<string, string> { ["Code"] = LatestCodeFor(email) });

        // Post the "turn off" form directly, even though the page no longer shows it.
        HttpResponseMessage response = await FormHelper.PostFormAsync(client, "/Account/TwoStep", "/Account/TwoStep?handler=Disable",
            new Dictionary<string, string> { ["CurrentPassword"] = GoodPassword });
        string html = await response.Content.ReadAsStringAsync();

        Assert.Contains(AccountSecurityService.TwoStepRequiredMessage, html);

        using IServiceScope scope = _factory.Services.CreateScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser stored = (await userManager.FindByIdAsync(user.Id))!;
        Assert.True(stored.TwoFactorEnabled);
    }
}