using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.Infrastructure;
using Rroms.Tests.Menu;
using Rroms.Web.Constants;
using Rroms.Web.Data;
namespace Rroms.Tests.Reports;
/// <summary>Checks the report layout against the real Razor response.</summary>
public class ReportLayoutTests
{
    /// <summary>Administrators see encoded activity, resolved dates and no-store headers.</summary>
    [Fact]
    public async Task Layout_RendersSectionsAndExportDates()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var customer = await OrderTestData.AddCustomerAsync(db);
            customer.FullName = "Preview Guest";
            await OrderTestData.AddReservationAsync(db, customer.Id);
            await db.SaveChangesAsync();
        }
        var response = await client.GetAsync("/Admin/Reports?Filter.Period=Monthly&Filter.Anchor=2026-10-20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("reports-metrics", html);
        Assert.Contains("data-date-field=\"custom\"", html);
        foreach (string section in new[] { "reservations", "sales", "items", "customers" })
        {
            Assert.Contains($"id=\"tab-{section}\"", html);
        }
        Assert.Contains("Preview Guest", html);
        Assert.Contains("name=\"Filter.From\" value=\"2026-10-01\"", html);
        Assert.Contains("name=\"Filter.To\" value=\"2026-10-31\"", html);
        string? previewPath = Environment.GetEnvironmentVariable("REPORT_PREVIEW_PATH");
        if (!string.IsNullOrEmpty(previewPath))
        {
            await File.WriteAllTextAsync(previewPath, html);
        }
    }
}
