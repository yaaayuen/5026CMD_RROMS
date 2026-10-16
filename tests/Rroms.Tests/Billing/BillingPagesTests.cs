using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.Infrastructure;
using Rroms.Tests.Menu;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.Billing;

/// <summary>DA-1 HTTP tests: real login, antiforgery, access rules, payment forms and receipt ownership.</summary>
public class BillingPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";
    private const string BillingUrl = "/Staff/Billing";
    private readonly TestWebApplicationFactory _factory;
    /// <summary>Receives the test application with its isolated SQLite database.</summary>
    public BillingPagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>All billing and receipt routes require authentication.</summary>
    [Theory]
    [InlineData("/Staff/Billing")]
    [InlineData("/Staff/Billing/Details/1")]
    [InlineData("/Staff/Billing/Receipt/1")]
    [InlineData("/Customer/Receipts/Details/1")]
    public async Task AnonymousIsSentToLogin(string url)
    {
        using HttpClient client = _factory.CreateSecureClient();
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    /// <summary>Customers cannot use the staff billing area.</summary>
    [Theory]
    [InlineData("/Staff/Billing")]
    [InlineData("/Staff/Billing/Details/1")]
    [InlineData("/Staff/Billing/Receipt/1")]
    public async Task CustomerIsDeniedStaffRoutes(string url)
    {
        var (client, _) = await SignInAsync(RoleNames.Customer);
        using (client)
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
        }
    }

    /// <summary>Both staff and administrators can open billing.</summary>
    [Theory]
    [InlineData(RoleNames.Staff)]
    [InlineData(RoleNames.Administrator)]
    public async Task EmployeeCanOpenBilling(string role)
    {
        var (client, _) = await SignInAsync(role);
        using (client)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(BillingUrl)).StatusCode);
            string home = await client.GetStringAsync("/Staff");
            Assert.Contains("href=\"/Staff/Billing\"", home);
        }
    }

    /// <summary>Each supported method creates a receipt, ignoring forged amount and recorder fields.</summary>
    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.CardTerminal)]
    [InlineData(PaymentMethod.EWallet)]
    [InlineData(PaymentMethod.SimulatedOnline)]
    public async Task PaymentPostUsesTrustedAmountAndIdentity(PaymentMethod method)
    {
        var (staffClient, staff) = await SignInAsync(RoleNames.Staff);
        Order order = await AddOrderAsync();
        using (staffClient)
        {
            string url = $"{BillingUrl}/Details/{order.Id}";
            var fields = new Dictionary<string, string>
            {
                ["Input.Method"] = method.ToString(),
                ["Amount"] = "0.01",
                ["Input.Amount"] = "0.01",
                ["StaffId"] = order.CustomerId,
                ["Input.StaffId"] = order.CustomerId
            };
            var response = await FormHelper.PostFormAsync(staffClient, url, url, fields);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains($"{BillingUrl}/Receipt/", response.Headers.Location?.ToString());
            using IServiceScope scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Payment payment = await db.Payments.SingleAsync(p => p.OrderId == order.Id);
            Assert.Equal(order.TotalAmount, payment.Amount);
            Assert.Equal(staff.Id, payment.StaffId);
            Notification notification = await db.Notifications.SingleAsync(n =>
                n.UserId == order.CustomerId && n.Type == NotificationType.PaymentConfirmed);
            Assert.Equal($"/Customer/Receipts/Details/{payment.Id}", notification.LinkUrl);
            string receipt = await staffClient.GetStringAsync(response.Headers.Location);
            Assert.Contains("40.00", receipt);
            Assert.Contains("Print receipt", receipt);
            if (method == PaymentMethod.SimulatedOnline)
            {
                Assert.Contains("DEMO ONLY", receipt);
                Assert.Contains("no real payment processed", receipt);
                Assert.Contains("no real payment processed", notification.Message);
            }
            // A repeated valid POST must not create another payment or confirmation.
            var repeated = await FormHelper.PostFormAsync(staffClient, url, url, fields);
            Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
            Assert.Equal(1, await db.Payments.CountAsync(p => p.OrderId == order.Id));
            Assert.Equal(1, await db.Notifications.CountAsync(n =>
                n.UserId == order.CustomerId && n.Type == NotificationType.PaymentConfirmed));
        }
    }

    /// <summary>Invalid or omitted methods redisplay validation and do not save a payment.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("999")]
    public async Task InvalidMethodSavesNothing(string method)
    {
        var (client, _) = await SignInAsync(RoleNames.Staff);
        Order order = await AddOrderAsync();
        using (client)
        {
            string url = $"{BillingUrl}/Details/{order.Id}";
            var response = await FormHelper.PostFormAsync(client, url, url,
                new Dictionary<string, string> { ["Input.Method"] = method });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using IServiceScope scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.Payments.AnyAsync(p => p.OrderId == order.Id));
        }
    }

    /// <summary>A request without a Razor Pages antiforgery token is rejected.</summary>
    [Fact]
    public async Task MissingAntiforgeryTokenIsRejected()
    {
        var (client, _) = await SignInAsync(RoleNames.Staff);
        Order order = await AddOrderAsync();
        using (client)
        {
            var response = await client.PostAsync($"{BillingUrl}/Details/{order.Id}",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["Input.Method"] = "Cash" }));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    /// <summary>Opening a bill never creates a payment, and uncompleted orders have no payment button.</summary>
    [Fact]
    public async Task BillGetIsReadOnlyAndUncompletedOrderCannotBePaid()
    {
        var (client, _) = await SignInAsync(RoleNames.Staff);
        Order order = await AddOrderAsync(completed: false);
        using (client)
        {
            var response = await client.GetAsync($"{BillingUrl}/Details/{order.Id}");
            string html = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Only completed orders can be paid", html);
            Assert.DoesNotContain(">Record payment</button>", html);
            using IServiceScope scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.Payments.AnyAsync(p => p.OrderId == order.Id));
        }
    }

    /// <summary>A receipt is visible to its owner only; another customer's receipt and a missing one return 404.</summary>
    [Fact]
    public async Task CustomerReceiptEnforcesOwnership()
    {
        var (ownerClient, owner) = await SignInAsync(RoleNames.Customer);
        var (strangerClient, _) = await SignInAsync(RoleNames.Customer);
        var (staffClient, _) = await SignInAsync(RoleNames.Staff);
        using (ownerClient)
        using (strangerClient)
        using (staffClient)
        {
            Order order = await AddOrderAsync(owner.Id);
            string billUrl = $"{BillingUrl}/Details/{order.Id}";
            await FormHelper.PostFormAsync(staffClient, billUrl, billUrl,
                new Dictionary<string, string> { ["Input.Method"] = "Cash" });
            using IServiceScope scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Payment payment = await db.Payments.SingleAsync(p => p.OrderId == order.Id);
            string url = $"/Customer/Receipts/Details/{payment.Id}";
            Assert.Equal(HttpStatusCode.OK, (await ownerClient.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await strangerClient.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await ownerClient.GetAsync($"/Customer/Receipts/Details/{int.MaxValue}")).StatusCode);
        }
    }

    private async Task<(HttpClient Client, ApplicationUser User)> SignInAsync(string role)
    {
        string email = $"billing-{Guid.NewGuid():N}@threegrills.test";
        ApplicationUser user = await _factory.CreateUserAsync(email, Password, role);
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);
        return (client, user);
    }

    private async Task<Order> AddOrderAsync(string? ownerId = null, bool completed = true)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        MenuItem dish = await OrderTestData.AddDishAsync(db, $"Receipt dish {Guid.NewGuid():N}");
        Order order = await OrderTestData.AddOrderAsync(db, dish.Id);
        if (ownerId != null)
        {
            order.CustomerId = ownerId;
            Reservation reservation = await db.Reservations.SingleAsync(r => r.Id == order.ReservationId);
            reservation.CustomerId = ownerId;
        }
        if (completed)
        {
            order.Status = OrderStatus.Completed;
        }
        await db.SaveChangesAsync();
        return order;
    }
}
