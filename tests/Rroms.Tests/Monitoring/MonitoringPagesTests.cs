using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.Infrastructure;
using Rroms.Tests.Menu;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Data;
using Rroms.Web.Contracts;
using Rroms.Web.Models;
namespace Rroms.Tests.Monitoring;
/// <summary>DA-7 integration tests for role policies, rendered content and invalid inputs.</summary>
public class MonitoringPagesTests : IDisposable
{
    private const string DailyUrl = "/Staff/Reservations/History";
    private const string Password = "Grill!Test2026";
    private readonly TestWebApplicationFactory _factory = new TestWebApplicationFactory();
    /// <summary>Releases the test application.</summary>
    public void Dispose()
    {
        _factory.Dispose();
    }
    /// <summary>Anonymous visitors must sign in before either staff page can be viewed.</summary>
    [Theory]
    [InlineData("/Staff/Reservations/History")]
    [InlineData("/Staff/Reservations/CustomerHistory/1")]
    public async Task Anonymous_IsRedirectedToLogin(string url)
    {
        using HttpClient client = _factory.CreateSecureClient();
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }
    /// <summary>Customers cannot inspect either staff page, even by guessing reservation IDs.</summary>
    [Theory]
    [InlineData("/Staff/Reservations/History")]
    [InlineData("/Staff/Reservations/CustomerHistory/1")]
    public async Task Customer_IsDenied(string url)
    {
        using HttpClient client = await SignInAsync(RoleNames.Customer);
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
    }
    /// <summary>Both authorized roles see the daily page, encoded notes and the matching customer's history.</summary>
    [Theory]
    [InlineData(RoleNames.Staff)]
    [InlineData(RoleNames.Administrator)]
    public async Task AuthorizedRole_SeesDailyAndHistoryWithEncodedNotes(string role)
    {
        using HttpClient client = await SignInAsync(role);
        int id;
        DateTime start = DateTime.UtcNow.AddDays(-2);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var a = await OrderTestData.AddCustomerAsync(db);
            a.FullName = "DA7 Guest";
            a.DietaryConsentAtUtc = DateTime.UtcNow;
            var tag = (await db.DietaryTags.FindAsync(2))!;
            tag.Name = "<script>preferences</script>";
            a.AvoidedDietaryTags.Add(tag);
            var booking = await OrderTestData.AddReservationAsync(db, a.Id);
            booking.StartUtc = start;
            booking.EndUtc = start.AddHours(2);
            booking.SpecialRequests = "<script>requests</script>";
            await db.SaveChangesAsync();
            id = booking.Id;
        }
        string date = MalaysiaTime.FromUtc(start).ToString("yyyy-MM-dd");
        var response = await client.GetAsync($"{DailyUrl}?Filter.Date={date}&Filter.Status=Confirmed&Filter.AreaId=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("DA7 Guest", html);
        Assert.Contains("&lt;script&gt;requests&lt;/script&gt;", html);
        Assert.Contains("&lt;script&gt;preferences&lt;/script&gt;", html);
        Assert.Contains($"/Staff/Reservations/CustomerHistory/{id}", html);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Contains($"href=\"/Staff/Reservations?Date={date}\"", html);
        string history = await client.GetStringAsync($"/Staff/Reservations/CustomerHistory/{id}");
        Assert.Contains("DA7 Guest", history);
        Assert.Contains("&lt;script&gt;requests&lt;/script&gt;", history);
        Assert.DoesNotContain("<script>requests</script>", history);
        string dashboard = await client.GetStringAsync("/Staff");
        Assert.Contains("href=\"/Staff/Reservations\"", dashboard);
        Assert.Contains("href=\"/Staff/Reservations/History\"", dashboard);
        Assert.Contains("href=\"/Staff/Billing\"", dashboard);
    }
    /// <summary>Malformed and invalid filters display validation rather than customer data or an exception.</summary>
    [Theory]
    [InlineData("Filter.Date=not-a-date")]
    [InlineData("Filter.Date=0001-01-01")]
    [InlineData("Filter.Date=9999-12-31")]
    [InlineData("Filter.Status=999")]
    [InlineData("Filter.Status=invalid")]
    [InlineData("Filter.AreaId=-1")]
    [InlineData("Filter.AreaId=bad")]
    public async Task BadFilters_ShowValidation(string query)
    {
        using HttpClient client = await SignInAsync(RoleNames.Staff);
        var response = await client.GetAsync($"{DailyUrl}?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation-summary-errors", html);
        Assert.DoesNotContain("<tbody>", html);
    }
    /// <summary>The initial date is Malaysia's current date and missing bookings return 404.</summary>
    [Fact]
    public async Task DefaultDate_AndMissingHistory()
    {
        using HttpClient client = await SignInAsync(RoleNames.Staff);
        string before = MalaysiaTime.FromUtc(DateTime.UtcNow).ToString("yyyy-MM-dd");
        string html = await client.GetStringAsync(DailyUrl);
        string after = MalaysiaTime.FromUtc(DateTime.UtcNow).ToString("yyyy-MM-dd");
        Assert.True(html.Contains($"value=\"{before}\"") || html.Contains($"value=\"{after}\""));
        Assert.Contains("No reservations match these filters", html);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Staff/Reservations/CustomerHistory/2147483647")).StatusCode);
    }
    /// <summary>Darren's row links to WL's selected day, where confirmation updates the booking and the filtered view.</summary>
    [Fact]
    public async Task ManageLink_UsesWLConfirmationAndRefreshesDA7()
    {
        using HttpClient client = await SignInAsync(RoleNames.Staff);
        int id;
        DateTime start = DateTime.UtcNow.AddDays(3);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var customer = await OrderTestData.AddCustomerAsync(db);
            var booking = await OrderTestData.AddReservationAsync(db, customer.Id, ReservationStatus.Pending);
            booking.StartUtc = start;
            booking.EndUtc = start.AddHours(2);
            await db.SaveChangesAsync();
            id = booking.Id;
        }
        string date = MalaysiaTime.FromUtc(start).ToString("yyyy-MM-dd");
        string html = await client.GetStringAsync($"{DailyUrl}?Filter.Date={date}&Filter.Status=Pending");
        Assert.Contains($"/Staff/Reservations?Date={date}", html);
        string wlUrl = $"/Staff/Reservations?Date={date}";
        string wlHtml = await client.GetStringAsync(wlUrl);
        Assert.Contains("Confirm", wlHtml);
        var response = await FormHelper.PostFormAsync(client, wlUrl,
            $"/Staff/Reservations?handler=Move&Date={date}",
            new Dictionary<string, string> { ["id"] = id.ToString(), ["to"] = "Confirmed" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(ReservationStatus.Confirmed, (await db.Reservations.FindAsync(id))!.Status);
        }
        string pending = await client.GetStringAsync($"{DailyUrl}?Filter.Date={date}&Filter.Status=Pending");
        Assert.Contains("No reservations match these filters", pending);
        string confirmed = await client.GetStringAsync($"{DailyUrl}?Filter.Date={date}&Filter.Status=Confirmed");
        Assert.Contains($"/Staff/Reservations/CustomerHistory/{id}", confirmed);
    }
    private async Task<HttpClient> SignInAsync(string role)
    {
        HttpClient client = _factory.CreateSecureClient();
        string email = $"da7-{Guid.NewGuid():N}@threegrills.test";
        await _factory.CreateUserAsync(email, Password, role);
        Assert.Equal(HttpStatusCode.Redirect, (await FormHelper.LoginAsync(client, email, Password)).StatusCode);
        return client;
    }
}
