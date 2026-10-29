using System.Net;
using System.Text.Json;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Common;
using Rroms.Web.Constants;

namespace Rroms.Tests.DiningExperience;

/// <summary>The floor plan on the Book a table page and the admin layout editor (Alex, Part 20).</summary>
public class FloorPlanPageTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";
    private const string LayoutUrl = "/Admin/Restaurant/Layout";
    private static readonly DateOnly VisitDate = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow)).AddDays(9);

    private readonly TestWebApplicationFactory _factory;

    public FloorPlanPageTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BookPage_AfterASearch_ShowsThePlanWithFreeAndTooSmallTables()
    {
        HttpClient customer = await SignInAsync(RoleNames.Customer);

        string html = await customer.GetStringAsync($"/Customer/Reservations/Book?Search.Date={VisitDate:yyyy-MM-dd}&Search.Time=19:00&Search.PartySize=3&Find=true");

        Assert.Contains("data-floor-plan=\"pick\"", html);
        Assert.Contains("id=\"floorPlanData\"", html);
        Assert.Contains("\"label\":\"T01\",\"capacity\":2", html);
        Assert.Contains("\"state\":\"Small\"", html);   // the 2-seat tables cannot take 3 guests
        Assert.Contains("\"state\":\"Free\"", html);
        Assert.Contains("name=\"DiningTableId\"", html);   // the list stays as the accessible fallback
    }

    [Fact]
    public async Task BookPage_BeforeASearch_HasNoPlan()
    {
        HttpClient customer = await SignInAsync(RoleNames.Customer);

        string html = await customer.GetStringAsync("/Customer/Reservations/Book");

        Assert.DoesNotContain("data-floor-plan", html);
    }

    [Fact]
    public async Task LayoutEditor_ForAnAdministrator_ShowsTheAreaAndItsTables()
    {
        HttpClient admin = await SignInAsync(RoleNames.Administrator);

        string html = await admin.GetStringAsync(LayoutUrl + "?areaId=2");

        Assert.Contains("data-floor-plan=\"edit\"", html);
        Assert.Contains("\"name\":\"Patio\"", html);
        Assert.Contains("\"label\":\"P01\"", html);
        Assert.Contains(">Floor plan</a>", html);   // link in the restaurant setup menu
    }

    [Theory]
    [InlineData(RoleNames.Staff)]
    [InlineData(RoleNames.Customer)]
    public async Task LayoutEditor_IsForAdministratorsOnly(string role)
    {
        HttpClient client = await SignInAsync(role);

        HttpResponseMessage response = await client.GetAsync(LayoutUrl);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LayoutEditor_SaveWithTouchingTables_ShowsTheProblem()
    {
        HttpClient admin = await SignInAsync(RoleNames.Administrator);
        // P01 (3x3) at (3,3) and P02 (4x4) moved to (6,3): no clear box between them.
        string json = JsonSerializer.Serialize(new[]
        {
            new { id = 7, label = "P01", capacity = 2, x = 3, y = 3, rotation = 0, isActive = true },
            new { id = 8, label = "P02", capacity = 4, x = 6, y = 3, rotation = 0, isActive = true }
        });

        HttpResponseMessage response = await FormHelper.PostFormAsync(admin, LayoutUrl + "?areaId=2", LayoutUrl + "?areaId=2",
            new Dictionary<string, string> { ["LayoutJson"] = json });
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Tables P01 and P02 need at least one clear box between them.", html);
    }

    [Fact]
    public async Task LayoutEditor_SaveValidLayout_RedirectsWithAConfirmation()
    {
        HttpClient admin = await SignInAsync(RoleNames.Administrator);
        string json = JsonSerializer.Serialize(new[]
        {
            new { id = 7, label = "P01", capacity = 2, x = 3, y = 3, rotation = 0, isActive = true },
            new { id = 8, label = "P02", capacity = 4, x = 8, y = 3, rotation = 0, isActive = true }
        });

        HttpResponseMessage response = await FormHelper.PostFormAsync(admin, LayoutUrl + "?areaId=2", LayoutUrl + "?areaId=2",
            new Dictionary<string, string> { ["LayoutJson"] = json });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        string html = await admin.GetStringAsync(response.Headers.Location!.ToString());
        Assert.Contains("Layout of Patio saved.", html);
    }

    private async Task<HttpClient> SignInAsync(string role)
    {
        HttpClient client = _factory.CreateSecureClient();
        if (role == RoleNames.Administrator)
        {
            await FormHelper.LoginAsync(client, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
            return client;
        }

        string email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@threegrills.test";
        await _factory.CreateUserAsync(email, Password, role);
        await FormHelper.LoginAsync(client, email, Password);
        return client;
    }
}
