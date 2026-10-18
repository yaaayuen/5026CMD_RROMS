using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Tests.Menu;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.History;

/// <summary>
/// YJ-5 acceptance for the "My history" page: who may open it, that a customer sees only their own records, and
/// that empty sections explain themselves. Owner: YJ.
/// </summary>
/// <remarks>
/// Integration tests in the same style as OrderPagesTests: the whole app starts in memory and tests sign in through
/// the real login form. All tests in this class share one database, so each test uses its own customers, and the
/// totals are unusual numbers that cannot appear anywhere else on the page.
/// </remarks>
public class CustomerHistoryPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";
    private const string HistoryUrl = "/Customer/History";

    private static readonly DateTime Placed = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public CustomerHistoryPagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ============================== Access control ==============================//

    /// <summary>Someone who is not signed in is sent to the login page.</summary>
    [Fact]
    public async Task Anonymous_IsSentToLogin()
    {
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await client.GetAsync(HistoryUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    /// <summary>Staff and administrators are signed in but are not customers, so they are refused.</summary>
    [Theory]
    [InlineData(RoleNames.Staff)]
    [InlineData(RoleNames.Administrator)]
    public async Task NonCustomer_IsDenied(string roleName)
    {
        SignedInUser user = await SignInNewUserAsync(roleName);

        HttpResponseMessage response = await user.Client.GetAsync(HistoryUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    // ============================== What the customer sees ==============================//

    /// <summary>A customer sees their own order and payment, and none of another customer's.</summary>
    [Fact]
    public async Task Customer_SeesOwnOrdersAndPayments_NeverSomeoneElses()
    {
        SignedInUser anna = await SignInNewUserAsync(RoleNames.Customer);
        SignedInUser ben = await SignInNewUserAsync(RoleNames.Customer);
        Order annasOrder = await AddPaidOrderAsync(anna.Id, 123.45m);
        await AddPaidOrderAsync(ben.Id, 678.90m);

        string html = await anna.Client.GetStringAsync(HistoryUrl);

        Assert.Contains("123.45", html);
        Assert.DoesNotContain("678.90", html);
        Assert.Contains($"/Customer/Orders/Details/{annasOrder.Id}", html);
    }

    /// <summary>A customer's reservation appears in "My reservations" with a link to the booking.</summary>
    [Fact]
    public async Task Customer_SeesOwnReservation()
    {
        SignedInUser anna = await SignInNewUserAsync(RoleNames.Customer);
        Reservation reservation = await AddReservationAsync(anna.Id);

        string html = await anna.Client.GetStringAsync(HistoryUrl);

        Assert.Contains("My reservations", html);
        Assert.Contains($"/Customer/Reservations/Details/{reservation.Id}", html);
        Assert.Contains(OrderTestData.SeededTableLabel, html);
    }

    /// <summary>The payment row shows the method in plain words.</summary>
    [Fact]
    public async Task Payment_ShowsTheMethodInPlainWords()
    {
        SignedInUser anna = await SignInNewUserAsync(RoleNames.Customer);
        await AddPaidOrderAsync(anna.Id, 55.55m, PaymentMethod.EWallet);

        string html = await anna.Client.GetStringAsync(HistoryUrl);

        Assert.Contains("My payments", html);
        Assert.Contains("E-wallet", html);
        Assert.Contains("55.55", html);
    }

    /// <summary>A new customer sees all three sections with a friendly "nothing yet" message.</summary>
    [Fact]
    public async Task NewCustomer_SeesNothingYetMessages()
    {
        SignedInUser anna = await SignInNewUserAsync(RoleNames.Customer);

        string html = await anna.Client.GetStringAsync(HistoryUrl);

        Assert.Contains("No reservations yet.", html);
        Assert.Contains("No orders yet.", html);
        Assert.Contains("No payments yet.", html);
    }

    // ============================== Helpers ==============================//

    private record SignedInUser(HttpClient Client, string Id);

    private async Task<SignedInUser> SignInNewUserAsync(string roleName)
    {
        string email = $"{roleName.ToLowerInvariant()}-{Guid.NewGuid():N}@threegrills.test";
        ApplicationUser user = await _factory.CreateUserAsync(email, Password, roleName);

        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);

        return new SignedInUser(client, user.Id);
    }

    private async Task<Reservation> AddReservationAsync(string customerId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await HistoryTestData.AddReservationAsync(context, customerId, Placed);
    }

    /// <summary>Saves a completed order with a Paid payment for a customer.</summary>
    private async Task<Order> AddPaidOrderAsync(string customerId, decimal total, PaymentMethod method = PaymentMethod.Cash)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        ApplicationUser staff = await HistoryTestData.AddStaffAsync(context);
        Order order = await HistoryTestData.AddOrderAsync(context, customerId, Placed, OrderStatus.Completed, total);
        await HistoryTestData.AddPaymentAsync(context, order, staff.Id, Placed.AddHours(1), PaymentStatus.Paid, method);

        return order;
    }
}
