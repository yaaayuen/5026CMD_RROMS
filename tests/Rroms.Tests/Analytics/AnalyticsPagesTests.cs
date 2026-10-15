using System.Net;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Services;

namespace Rroms.Tests.Analytics;

/// <summary>
/// WL-4 acceptance for the admin Occupancy analytics page: who can open it, the dashboard tile,
/// the default range, a chosen range, and a refused range.
/// </summary>
/// <remarks>
/// Integration tests on the running app (TestWebApplicationFactory), like the WL-1 page tests.
/// The figures themselves are checked in AnalyticsServiceTests; here we check the page shows them.
/// </remarks>
public class AnalyticsPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";
    private const string AnalyticsUrl = "/Admin/Analytics";

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public AnalyticsPagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Someone who is not signed in is sent to the login page.</summary>
    [Fact]
    public async Task Anonymous_IsSentToLogin()
    {
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await client.GetAsync(AnalyticsUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    /// <summary>Customers and staff cannot see business analytics.</summary>
    [Theory]
    [InlineData(RoleNames.Customer)]
    [InlineData(RoleNames.Staff)]
    public async Task NonAdministrator_IsDenied(string roleName)
    {
        HttpClient client = await SignInAsNewUserAsync(roleName);

        HttpResponseMessage response = await client.GetAsync(AnalyticsUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    /// <summary>The "Analytics dashboard" tile on the admin home page now links here.</summary>
    [Fact]
    public async Task AdminDashboard_LinksToAnalytics()
    {
        HttpClient client = await SignInAsAdministratorAsync();

        string html = await client.GetStringAsync("/Admin");

        Assert.Contains($"href=\"{AnalyticsUrl}\"", html);
    }

    /// <summary>Without a range the page opens on the default range and explains the utilisation formula.</summary>
    [Fact]
    public async Task Administrator_DefaultRange_ShowsReportAndFormula()
    {
        HttpClient client = await SignInAsAdministratorAsync();

        HttpResponseMessage response = await client.GetAsync(AnalyticsUrl);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Table utilisation", html);
        Assert.Contains("How utilisation is worked out", html);
        Assert.Contains("Peak hours", html);
        Assert.Contains("Reservation trend", html);
    }

    /// <summary>A chosen range is kept in the form and every day of it appears in the trend.</summary>
    [Fact]
    public async Task Administrator_ChosenRange_IsKeptAndListed()
    {
        HttpClient client = await SignInAsAdministratorAsync();

        string html = await client.GetStringAsync($"{AnalyticsUrl}?From=2026-10-05&To=2026-10-11");

        Assert.Contains("value=\"2026-10-05\"", html);
        Assert.Contains("value=\"2026-10-11\"", html);
        Assert.Contains("Mon 5 Oct", html);
        Assert.Contains("Sun 11 Oct", html);
        Assert.DoesNotContain("Mon 12 Oct", html);
    }

    /// <summary>An end date before the start date shows the service's message instead of a report.</summary>
    [Fact]
    public async Task Administrator_EndBeforeStart_ShowsError()
    {
        HttpClient client = await SignInAsAdministratorAsync();

        HttpResponseMessage response = await client.GetAsync($"{AnalyticsUrl}?From=2026-10-11&To=2026-10-05");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(AnalyticsService.EndBeforeStartMessage, html);
        Assert.DoesNotContain("Table utilisation", html);
    }

    // ============================== Helpers ==============================//

    /// <summary>Returns a client signed in as the administrator the test seeder creates.</summary>
    private async Task<HttpClient> SignInAsAdministratorAsync()
    {
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        return client;
    }

    /// <summary>Creates a new user with the given role and returns a client signed in as them.</summary>
    private async Task<HttpClient> SignInAsNewUserAsync(string roleName)
    {
        string email = $"{roleName.ToLowerInvariant()}-{Guid.NewGuid():N}@threegrills.test";
        await _factory.CreateUserAsync(email, Password, roleName);

        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);
        return client;
    }
}
