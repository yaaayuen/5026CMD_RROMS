using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.Infrastructure;
using Rroms.Tests.Menu;
using Rroms.Web.Constants;
using Rroms.Web.Data;
namespace Rroms.Tests.Reports;
/// <summary>Real Razor rendering and the inherited administrator policy.</summary>
public class ReportPagesTests
{
    /// <summary>Only administrators can read reports.</summary>
    [Theory]
    [InlineData(null, "/Account/Login")]
    [InlineData(RoleNames.Customer, "AccessDenied")]
    [InlineData(RoleNames.Staff, "AccessDenied")]
    public async Task Restricted(string? role, string redirect)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateSecureClient();
        if (role != null)
        {
            string email = $"report-{Guid.NewGuid():N}@threegrills.test";
            await factory.CreateUserAsync(email, "Grill!Test2026", role);
            await FormHelper.LoginAsync(client, email, "Grill!Test2026");
        }
        var response = await client.GetAsync("/Admin/Reports");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(redirect, response.Headers.Location!.ToString());
    }
    /// <summary>Administrators see encoded activity, resolved dates and no-store headers.</summary>
    [Fact]
    public async Task AdministratorReport()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var customer = await OrderTestData.AddCustomerAsync(db);
            customer.FullName = "<script>guest</script>";
            await OrderTestData.AddReservationAsync(db, customer.Id);
            await db.SaveChangesAsync();
        }
        var response = await client.GetAsync("/Admin/Reports?Filter.Period=Weekly&Filter.Anchor=2026-10-20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("19 Oct 2026", html); Assert.Contains("25 Oct 2026", html);
        Assert.Contains("&lt;script&gt;guest&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>guest</script>", html);
        Assert.DoesNotContain("Demo payments", html);
        // The dashboard groups Reports under "Insights and reports".
        Assert.Contains("href=\"/Admin/Reports\"", await client.GetStringAsync("/Admin/Insights"));
    }
    /// <summary>Malformed and unsupported filters show errors without report rows.</summary>
    [Theory]
    [InlineData("Filter.Anchor=bad")]
    [InlineData("Filter.Period=999")]
    [InlineData("Filter.Period=Custom&Filter.From=2026-10-20&Filter.To=2026-10-01")]
    public async Task InvalidFilters(string query)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        string html = await client.GetStringAsync("/Admin/Reports?" + query);
        Assert.Contains("validation-summary-errors", html);
        Assert.DoesNotContain("<tbody>", html);
    }
}
