using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Models;
using Rroms.Web.Services;

namespace Rroms.Tests.Restaurant;

/// <summary>
/// WL-1 acceptance for the five admin restaurant setup pages: who can open them, and that the
/// forms really save (or refuse) through the running application. Owner: WL.
/// </summary>
/// <remarks>
/// These are integration tests: they start the whole app in memory (TestWebApplicationFactory),
/// sign in through the real login form and send real HTTP requests, so routing, authorization,
/// antiforgery tokens, model binding and the service are all exercised together.
/// </remarks>
public class RestaurantPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";

    private const string DetailsUrl = "/Admin/Restaurant";
    private const string HoursUrl = "/Admin/Restaurant/Hours";
    private const string AreasUrl = "/Admin/Restaurant/Areas";
    private const string TablesUrl = "/Admin/Restaurant/Tables";
    private const string CategoriesUrl = "/Admin/Restaurant/Categories";

    /// <summary>An area Id that the seed data never creates.</summary>
    private const int MissingAreaId = 999;

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public RestaurantPagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ============================== Access control ==============================//

    /// <summary>The public Opening hours page works without signing in and shows the weekly hours.</summary>
    [Fact]
    public async Task OpeningHoursPage_Anonymous_ShowsWeeklyHours()
    {
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await client.GetAsync("/Visit");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Opening hours", html);
        Assert.Contains("Monday", html);
        Assert.Contains("11:00 AM", html);
    }

    /// <summary>Someone who is not signed in is sent to the login page.</summary>
    [Theory]
    [InlineData(DetailsUrl)]
    [InlineData(HoursUrl)]
    [InlineData(AreasUrl)]
    [InlineData(TablesUrl)]
    [InlineData(CategoriesUrl)]
    public async Task Anonymous_IsSentToLogin(string url)
    {
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    /// <summary>Customers and staff are signed in but are not administrators, so they are refused.</summary>
    [Theory]
    [InlineData(RoleNames.Customer, DetailsUrl)]
    [InlineData(RoleNames.Customer, TablesUrl)]
    [InlineData(RoleNames.Staff, HoursUrl)]
    [InlineData(RoleNames.Staff, CategoriesUrl)]
    public async Task NonAdministrator_IsDenied(string roleName, string url)
    {
        HttpClient client = await SignInAsNewUserAsync(roleName);

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    /// <summary>The administrator can open every page and sees its heading.</summary>
    [Theory]
    [InlineData(DetailsUrl, "Restaurant details")]
    [InlineData(HoursUrl, "Opening hours and closures")]
    [InlineData(AreasUrl, "Dining areas")]
    [InlineData(TablesUrl, "Every table customers can book")]
    [InlineData(CategoriesUrl, "Menu categories")]
    public async Task Administrator_CanOpenPage(string url, string expectedText)
    {
        HttpClient client = await SignInAsAdministratorAsync();

        HttpResponseMessage response = await client.GetAsync(url);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedText, html);
    }

    /// <summary>The "Restaurant setup" tile on the admin dashboard links to the setup pages.</summary>
    [Fact]
    public async Task AdminDashboard_LinksToRestaurantSetup()
    {
        HttpClient client = await SignInAsAdministratorAsync();

        string html = await client.GetStringAsync("/Admin");

        Assert.Contains($"href=\"{DetailsUrl}\"", html);
    }

    // ============================== Forms ==============================//

    /// <summary>Saving valid restaurant details redirects (Post-Redirect-Get) and the new name is shown.</summary>
    [Fact]
    public async Task Details_ValidPost_SavesAndRedirects()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        var fields = new Dictionary<string, string>
        {
            ["Input.Name"] = "Three Grills Bangsar",
            ["Input.Address"] = "2 Test Road, 59000 Kuala Lumpur",
            ["Input.Phone"] = "+60 3-1111 2222",
            ["Input.Email"] = "bangsar@threegrills.example"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(client, DetailsUrl, DetailsUrl, fields);
        string pageAfter = await client.GetStringAsync(DetailsUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Three Grills Bangsar", pageAfter);
    }

    /// <summary>An invalid email is refused: the page is shown again instead of redirecting.</summary>
    [Fact]
    public async Task Details_InvalidEmail_ShowsFormAgain()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        var fields = new Dictionary<string, string>
        {
            ["Input.Name"] = "Three Grills",
            ["Input.Address"] = "1 Example Street",
            ["Input.Phone"] = "+60 3-0000 0000",
            ["Input.Email"] = "abc"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(client, DetailsUrl, DetailsUrl, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Saving a day with an empty opening time is refused, so the stored hours do not change
    /// (regression test for the empty-time fix).
    /// </summary>
    [Fact]
    public async Task Hours_EmptyOpeningTime_DoesNotChangeTheDay()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        OperatingHour before = await GetDayAsync(DayOfWeek.Friday);
        var fields = new Dictionary<string, string>
        {
            ["day.DayOfWeek"] = nameof(DayOfWeek.Friday),
            ["day.OpensAt"] = string.Empty,
            ["day.ClosesAt"] = "22:00"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(
            client, HoursUrl, $"{HoursUrl}?handler=SaveDay", fields);
        OperatingHour after = await GetDayAsync(DayOfWeek.Friday);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(before.OpensAt, after.OpensAt);
        Assert.Equal(before.ClosesAt, after.ClosesAt);
    }

    /// <summary>A duplicate area name (different case) is refused and the service's message is shown.</summary>
    [Fact]
    public async Task Areas_DuplicateName_ShowsServiceError()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        var fields = new Dictionary<string, string>
        {
            ["Input.Name"] = "main hall",
            ["Input.IsActive"] = "true"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(client, AreasUrl, AreasUrl, fields);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(RestaurantService.DuplicateAreaNameMessage, html);
    }

    /// <summary>Editing an area that does not exist returns 404 Not Found.</summary>
    [Fact]
    public async Task Areas_EditMissingArea_ReturnsNotFound()
    {
        HttpClient client = await SignInAsAdministratorAsync();

        HttpResponseMessage response = await client.GetAsync($"{AreasUrl}/{MissingAreaId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
    /// <param name="roleName">For example <see cref="RoleNames.Customer"/>.</param>
    private async Task<HttpClient> SignInAsNewUserAsync(string roleName)
    {
        string email = $"{roleName.ToLowerInvariant()}-{Guid.NewGuid():N}@threegrills.test";
        await _factory.CreateUserAsync(email, Password, roleName);

        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);
        return client;
    }

    /// <summary>Reads one weekday's stored hours straight from the service.</summary>
    /// <param name="day">The weekday to read.</param>
    private async Task<OperatingHour> GetDayAsync(DayOfWeek day)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IRestaurantService service = scope.ServiceProvider.GetRequiredService<IRestaurantService>();
        IReadOnlyList<OperatingHour> hours = await service.GetOperatingHoursAsync();
        return hours.Single(h => h.DayOfWeek == day);
    }
}
