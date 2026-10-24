using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.Infrastructure;
using Rroms.Tests.Menu;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
namespace Rroms.Tests.AdminMonitoring;
/// <summary>DA-2 real HTTP tests for role access, input validation, totals and usable cross-area links.</summary>
public class AdminMonitoringPagesTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new TestWebApplicationFactory();
    private const string Password = "Grill!Test2026";
    /// <summary>Closes the private application.</summary>
    public void Dispose() { _factory.Dispose(); }
    /// <summary>All administrator monitoring pages redirect anonymous users to login.</summary>
    [Theory]
    [InlineData("Reservations")]
    [InlineData("Orders")]
    [InlineData("Transactions")]
    public async Task Anonymous_CannotRead(string page)
    {
        using var client = _factory.CreateSecureClient();
        var response = await client.GetAsync($"/Admin/Monitoring/{page}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }
    /// <summary>Staff and customers cannot access administrator monitoring.</summary>
    [Theory]
    [InlineData(RoleNames.Staff, "Reservations")]
    [InlineData(RoleNames.Staff, "Orders")]
    [InlineData(RoleNames.Staff, "Transactions")]
    [InlineData(RoleNames.Customer, "Reservations")]
    [InlineData(RoleNames.Customer, "Orders")]
    [InlineData(RoleNames.Customer, "Transactions")]
    public async Task OtherRoles_AreDenied(string role, string page)
    {
        using var client = await LoginAsync(role);
        var response = await client.GetAsync($"/Admin/Monitoring/{page}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
    }
    /// <summary>Administrators get today's Malaysia range, an empty-state message and no-store headers.</summary>
    [Theory]
    [InlineData("Reservations")]
    [InlineData("Orders")]
    [InlineData("Transactions")]
    public async Task Administrator_SeesDefaultsAndEmptyState(string page)
    {
        using var client = await LoginAsync(RoleNames.Administrator);
        string before = MalaysiaTime.FromUtc(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var response = await client.GetAsync($"/Admin/Monitoring/{page}");
        string after = MalaysiaTime.FromUtc(DateTime.UtcNow).ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"No {page.ToLowerInvariant()} match these filters", html);
        Assert.True(html.Contains($"value=\"{before}\"") || html.Contains($"value=\"{after}\""));
        Assert.True(response.Headers.CacheControl!.NoStore);
    }
    /// <summary>Invalid binding and service filters show validation with no data table.</summary>
    [Theory]
    [InlineData("Reservations", "Filter.From=bad")]
    [InlineData("Orders", "Filter.From=2026-10-23&Filter.To=2026-10-22")]
    [InlineData("Transactions", "Filter.To=9999-12-31")]
    [InlineData("Reservations", "Filter.AreaId=-1")]
    [InlineData("Orders", "Filter.Status=999")]
    [InlineData("Transactions", "Filter.Status=bad")]
    [InlineData("Transactions", "Filter.Method=999")]
    public async Task BadFilters_ShowValidation(string page, string query)
    {
        using var client = await LoginAsync(RoleNames.Administrator);
        var response = await client.GetAsync($"/Admin/Monitoring/{page}?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation-summary-errors", html);
        Assert.DoesNotContain("<tbody>", html);
    }
    /// <summary>Filtered pages show encoded names, exact totals and working WL/billing/receipt links.</summary>
    [Fact]
    public async Task Lists_ShowTotalsAndLinksThatAdministratorCanOpen()
    {
        using var client = await LoginAsync(RoleNames.Administrator);
        int orderId; int bookingId; int paymentId;
        const string date = "2026-10-22";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dish = await OrderTestData.AddDishAsync(db, "Test item");
            var order = await OrderTestData.AddOrderAsync(db, dish.Id);
            order.Status = OrderStatus.Completed; order.TotalAmount = 42.25m;
            DateTime when = MalaysiaTime.ToUtc(new DateTime(2026, 10, 22, 12, 0, 0));
            order.PlacedAtUtc = when;
            var booking = (await db.Reservations.FindAsync(order.ReservationId))!;
            booking.StartUtc = when; booking.EndUtc = when.AddHours(2);
            var customer = (await db.Users.FindAsync(order.CustomerId))!;
            customer.FullName = "<script>guest</script>";
            var payment = new Payment { OrderId = order.Id, Amount = 42.25m, Method = PaymentMethod.EWallet,
                Status = PaymentStatus.Paid, StaffId = customer.Id, PaidAtUtc = when };
            db.Payments.Add(payment); await db.SaveChangesAsync();
            orderId = order.Id; bookingId = booking.Id; paymentId = payment.Id;
        }
        string query = $"?Filter.From={date}&Filter.To={date}";
        string reservations = await client.GetStringAsync("/Admin/Monitoring/Reservations" + query + "&Filter.AreaId=1&Filter.Status=Confirmed");
        Assert.Contains($"/Staff/Reservations?Date={date}", reservations);
        Assert.Contains($"/Staff/Reservations/CustomerHistory/{bookingId}", reservations);
        Assert.Contains("&lt;script&gt;guest&lt;/script&gt;", reservations);
        string orders = await client.GetStringAsync("/Admin/Monitoring/Orders" + query + "&Filter.Status=Completed");
        Assert.Contains("42.25", orders);
        Assert.Contains($"/Staff/Billing/Details/{orderId}", orders);
        string transactions = await client.GetStringAsync("/Admin/Monitoring/Transactions" + query + "&Filter.Method=EWallet&Filter.Status=Paid");
        Assert.Contains("42.25", transactions);
        Assert.DoesNotContain("Simulated online (demo)", transactions);
        Assert.Contains($"/Staff/Billing/Receipt/{paymentId}", transactions);
        foreach (string url in new[] { $"/Staff/Reservations?Date={date}", $"/Staff/Reservations/CustomerHistory/{bookingId}",
            $"/Staff/Billing/Details/{orderId}", $"/Staff/Billing/Receipt/{paymentId}" })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
        }
        Assert.Contains("/Admin/Monitoring/Reservations", await client.GetStringAsync("/Admin"));
    }
    /// <summary>Voided records retain a receipt audit link but never contribute to the paid total.</summary>
    [Fact]
    public async Task VoidedTransaction_HasAuditReceiptLink()
    {
        using var client = await LoginAsync(RoleNames.Administrator);
        int paymentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dish = await OrderTestData.AddDishAsync(db, "Item");
            var order = await OrderTestData.AddOrderAsync(db, dish.Id);
            var payment = new Payment { OrderId = order.Id, StaffId = order.CustomerId,
                Amount = 99m, Method = PaymentMethod.Cash, Status = PaymentStatus.Voided, PaidAtUtc = DateTime.UtcNow };
            db.Payments.Add(payment); await db.SaveChangesAsync(); paymentId = payment.Id;
        }
        string html = await client.GetStringAsync("/Admin/Monitoring/Transactions?Filter.Status=Voided");
        Assert.Contains("99.00", html);
        Assert.Contains($"/Staff/Billing/Receipt/{paymentId}", html);
        Assert.Contains("RM 0.00", html);
    }
    private async Task<HttpClient> LoginAsync(string role)
    {
        var client = _factory.CreateSecureClient();
        string email = $"da2-{Guid.NewGuid():N}@threegrills.test";
        await _factory.CreateUserAsync(email, Password, role);
        Assert.Equal(HttpStatusCode.Redirect, (await FormHelper.LoginAsync(client, email, Password)).StatusCode);
        return client;
    }
}
