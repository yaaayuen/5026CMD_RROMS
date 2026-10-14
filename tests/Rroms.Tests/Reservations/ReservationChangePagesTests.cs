using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Tests.Reservations;

/// <summary>
/// WL-3 acceptance for the customer's Details and Change pages: ownership (404 for someone else's booking),
/// cancelling through the Cancel button, and changing a booking through the real form.
/// </summary>
/// <remarks>
/// Integration tests on the running app. All tests share one database, so each test books its own table
/// at its own time. Bookings are created through the service directly, which keeps each test short.
/// </remarks>
public class ReservationChangePagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";
    private const string DetailsUrl = "/Customer/Reservations/Details";
    private const string ChangeUrl = "/Customer/Reservations/Change";

    private const int TableT01Id = 1;
    private const int TableT02Id = 2;
    private const int TableT03Id = 3;
    private const int TableP01Id = 7;
    private const int TableP02Id = 8;

    /// <summary>A visit one week ahead (Malaysia), always in the future and inside the booking window.</summary>
    private static readonly DateOnly VisitDate = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow)).AddDays(7);

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public ReservationChangePagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ============================== Details ==============================//

    /// <summary>Someone who is not signed in is sent to the login page.</summary>
    [Fact]
    public async Task Details_Anonymous_IsSentToLogin()
    {
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await client.GetAsync($"{DetailsUrl}/1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    /// <summary>The owner sees their booking with the change and cancel buttons.</summary>
    [Fact]
    public async Task Details_OwnBooking_ShowsChangeAndCancel()
    {
        (HttpClient client, string customerId) = await SignInAsNewCustomerAsync();
        int id = await BookAsync(customerId, TableT01Id, new TimeOnly(12, 0));

        HttpResponseMessage response = await client.GetAsync($"{DetailsUrl}/{id}");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Change booking", html);
        Assert.Contains("Cancel booking", html);
    }

    /// <summary>Another customer gets 404, so they cannot even tell the booking exists.</summary>
    /// <remarks>Each case books at its own hour, because both cases share one database and one table.</remarks>
    [Theory]
    [InlineData(DetailsUrl, 12)]
    [InlineData(ChangeUrl, 16)]
    public async Task OtherCustomersBooking_ReturnsNotFound(string pageUrl, int hour)
    {
        (HttpClient _, string ownerId) = await SignInAsNewCustomerAsync();
        (HttpClient stranger, string _) = await SignInAsNewCustomerAsync();
        int id = await BookAsync(ownerId, TableT02Id, new TimeOnly(hour, 0));

        HttpResponseMessage response = await stranger.GetAsync($"{pageUrl}/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>The Cancel button cancels the booking and goes back to My reservations.</summary>
    [Fact]
    public async Task Details_CancelButton_CancelsTheBooking()
    {
        (HttpClient client, string customerId) = await SignInAsNewCustomerAsync();
        int id = await BookAsync(customerId, TableT03Id, new TimeOnly(12, 0));
        string page = $"{DetailsUrl}/{id}";

        HttpResponseMessage response = await FormHelper.PostFormAsync(
            client, page, $"{page}?handler=Cancel", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ReservationStatus.Cancelled, await StatusOfAsync(id));
    }

    // ============================== Change ==============================//

    /// <summary>Searching from the Change page still offers the booking's own table, marked as current.</summary>
    [Fact]
    public async Task Change_Search_OffersTheCurrentTable()
    {
        (HttpClient client, string customerId) = await SignInAsNewCustomerAsync();
        int id = await BookAsync(customerId, TableP01Id, new TimeOnly(14, 0));

        string html = await client.GetStringAsync(ChangeSearchUrl(id, new TimeOnly(14, 30)));

        Assert.Contains("Table P01", html);
        Assert.Contains("Your current table", html);
    }

    /// <summary>Saving the Change form moves the booking and returns to its Details page.</summary>
    [Fact]
    public async Task Change_SaveForm_MovesTheBooking()
    {
        (HttpClient client, string customerId) = await SignInAsNewCustomerAsync();
        int id = await BookAsync(customerId, TableP02Id, new TimeOnly(15, 0));
        TimeOnly newTime = new TimeOnly(15, 30);
        Dictionary<string, string> fields = new Dictionary<string, string>
        {
            ["Search.Date"] = VisitDate.ToString("yyyy-MM-dd"),
            ["Search.Time"] = newTime.ToString("HH:mm"),
            ["Search.PartySize"] = "2",
            ["DiningTableId"] = TableP02Id.ToString(),
            ["SpecialRequests"] = "Moved by test"
        };

        // The results page (Find=true) is the one that contains the POST form and its antiforgery token.
        HttpResponseMessage response = await FormHelper.PostFormAsync(
            client, ChangeSearchUrl(id, newTime), $"{ChangeUrl}/{id}", fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"{DetailsUrl}/{id}", response.Headers.Location?.ToString());
        DateTime startUtc = await StartOfAsync(id);
        Assert.Equal(MalaysiaTime.ToUtc(VisitDate.ToDateTime(newTime)), startUtc);
    }

    // ============================== Helpers ==============================//

    /// <summary>The Change page with a search already run for 2 guests at the given time.</summary>
    private static string ChangeSearchUrl(int id, TimeOnly time)
    {
        return $"{ChangeUrl}/{id}?Search.Date={VisitDate:yyyy-MM-dd}&Search.Time={time:HH\\:mm}&Search.PartySize=2&Find=true";
    }

    /// <summary>Creates a customer and returns a client signed in as them, plus their user ID.</summary>
    private async Task<(HttpClient Client, string CustomerId)> SignInAsNewCustomerAsync()
    {
        string email = $"customer-{Guid.NewGuid():N}@threegrills.test";
        ApplicationUser user = await _factory.CreateUserAsync(email, Password, RoleNames.Customer);

        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);
        return (client, user.Id);
    }

    /// <summary>Books a table for 2 guests on <see cref="VisitDate"/> through the service and returns its ID.</summary>
    private async Task<int> BookAsync(string customerId, int tableId, TimeOnly time)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IReservationService service = scope.ServiceProvider.GetRequiredService<IReservationService>();

        ServiceResult<int> result = await service.CreateReservationAsync(customerId, new BookingInput
        {
            Date = VisitDate,
            Time = time,
            PartySize = 2,
            DiningTableId = tableId
        });

        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        return result.Value;
    }

    /// <summary>The booking's status as stored now.</summary>
    private async Task<ReservationStatus> StatusOfAsync(int id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Reservations.Where(r => r.Id == id).Select(r => r.Status).SingleAsync();
    }

    /// <summary>The booking's start time (UTC) as stored now.</summary>
    private async Task<DateTime> StartOfAsync(int id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Reservations.Where(r => r.Id == id).Select(r => r.StartUtc).SingleAsync();
    }
}
