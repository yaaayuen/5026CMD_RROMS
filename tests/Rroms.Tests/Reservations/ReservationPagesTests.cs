using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Services;

namespace Rroms.Tests.Reservations;

/// <summary>
/// WL-2 acceptance for the customer booking pages: who can open them, a full booking through the
/// real form, the "just taken" message, ownership of "My reservations" and the staff notification.
/// </summary>
/// <remarks>
/// Integration tests: the whole app runs in memory (TestWebApplicationFactory) and the tests sign in
/// and send real HTTP requests. All tests in this class share one database, so each booking uses
/// its own table or its own customer to stay independent of the others.
/// </remarks>
public class ReservationPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";
    private const string BookUrl = "/Customer/Reservations/Book";
    private const string MyReservationsUrl = "/Customer/Reservations";

    /// <summary>Seeded table T01 (2 seats).</summary>
    private const int TableT01Id = 1;

    /// <summary>Seeded table T02 (2 seats).</summary>
    private const int TableT02Id = 2;

    /// <summary>Seeded table P01 (2 seats, Patio).</summary>
    private const int TableP01Id = 7;

    /// <summary>T06 in Main Hall, the only 8-seat table in the seed data.</summary>
    private const int TableT06Id = 6;

    /// <summary>A visit one week ahead (Malaysia), always in the future and inside the booking window.</summary>
    private static readonly DateOnly VisitDate = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow)).AddDays(7);

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public ReservationPagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ============================== Access control ==============================//

    /// <summary>Someone who is not signed in is sent to the login page.</summary>
    [Theory]
    [InlineData(BookUrl)]
    [InlineData(MyReservationsUrl)]
    public async Task Anonymous_IsSentToLogin(string url)
    {
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    /// <summary>Staff and administrators cannot use the customer booking pages.</summary>
    [Theory]
    [InlineData(RoleNames.Staff, BookUrl)]
    [InlineData(RoleNames.Administrator, MyReservationsUrl)]
    public async Task NonCustomer_IsDenied(string roleName, string url)
    {
        HttpClient client = await SignInAsNewUserAsync(roleName);

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    /// <summary>A customer can open both pages.</summary>
    [Theory]
    [InlineData(BookUrl, "Book a table")]
    [InlineData(MyReservationsUrl, "My reservations")]
    public async Task Customer_CanOpenPage(string url, string expectedText)
    {
        HttpClient client = await SignInAsNewUserAsync(RoleNames.Customer);

        HttpResponseMessage response = await client.GetAsync(url);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedText, html);
    }

    // ============================== Search and booking ==============================//

    /// <summary>"Find tables" for 7 guests offers only the 8-seat table.</summary>
    [Fact]
    public async Task Search_PartyOfSeven_ShowsOnlyTheLargeTable()
    {
        HttpClient client = await SignInAsNewUserAsync(RoleNames.Customer);
        string url = $"{BookUrl}?Search.Date={VisitDate:yyyy-MM-dd}&Search.Time=19:00&Search.PartySize=7&Find=true";

        string html = await client.GetStringAsync(url);

        Assert.Contains("Table T06", html);
        Assert.DoesNotContain("Table T01", html);
    }

    /// <summary>When the only table for 7 is taken, the page suggests other times that day instead of a dead end.</summary>
    [Fact]
    public async Task Search_TimeFullyBooked_SuggestsOtherFreeTimes()
    {
        HttpClient client = await SignInAsNewUserAsync(RoleNames.Customer);
        DateOnly day = VisitDate.AddDays(2);
        Dictionary<string, string> fields = new Dictionary<string, string>
        {
            ["Search.Date"] = day.ToString("yyyy-MM-dd"),
            ["Search.Time"] = "13:00",
            ["Search.PartySize"] = "7",
            ["DiningTableId"] = TableT06Id.ToString(),
            ["SpecialRequests"] = string.Empty
        };
        await FormHelper.PostFormAsync(client, BookUrl, BookUrl, fields);

        string html = await client.GetStringAsync($"{BookUrl}?Search.Date={day:yyyy-MM-dd}&Search.Time=13:00&Search.PartySize=7&Find=true");

        Assert.Contains("still have a free table", html);
        Assert.Contains("Search.Time=18:00", html);
    }

    /// <summary>A party bigger than the largest table is told to contact the restaurant.</summary>
    [Fact]
    public async Task Search_PartyBiggerThanLargestTable_AsksToContactRestaurant()
    {
        HttpClient client = await SignInAsNewUserAsync(RoleNames.Customer);

        string html = await client.GetStringAsync($"{BookUrl}?Search.Date={VisitDate:yyyy-MM-dd}&Search.Time=19:00&Search.PartySize=20&Find=true");

        Assert.Contains("Our largest table seats 8 guests", html);
    }

    /// <summary>
    /// A full booking through the form: it redirects to My reservations (Post-Redirect-Get),
    /// the booking is listed as Confirmed, and every active staff member is notified.
    /// </summary>
    [Fact]
    public async Task Book_ValidForm_SavesConfirmedBookingAndNotifiesStaff()
    {
        ApplicationUser staff = await _factory.CreateUserAsync(UniqueEmail(RoleNames.Staff), Password, RoleNames.Staff);
        HttpClient client = await SignInAsNewUserAsync(RoleNames.Customer);
        string note = $"Birthday {Guid.NewGuid():N}";

        HttpResponseMessage response = await PostBookingAsync(client, TableT01Id, note);
        string myReservations = await client.GetStringAsync(MyReservationsUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(MyReservationsUrl, response.Headers.Location?.ToString());
        Assert.Contains(note, myReservations);
        Assert.Contains("Confirmed", myReservations);
        Assert.True(await HasNotificationAsync(staff.Id, "New reservation"));
    }

    /// <summary>When the table was taken first, the second customer sees the friendly message, not an error page.</summary>
    [Fact]
    public async Task Book_TableAlreadyTaken_ShowsJustBookedMessage()
    {
        HttpClient first = await SignInAsNewUserAsync(RoleNames.Customer);
        HttpClient second = await SignInAsNewUserAsync(RoleNames.Customer);
        await PostBookingAsync(first, TableT02Id, null);

        HttpResponseMessage response = await PostBookingAsync(second, TableT02Id, null);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(ReservationService.TableJustTakenMessage, html);
    }

    /// <summary>My reservations only shows the signed-in customer's own bookings.</summary>
    [Fact]
    public async Task MyReservations_DoesNotShowOtherCustomersBookings()
    {
        HttpClient owner = await SignInAsNewUserAsync(RoleNames.Customer);
        HttpClient someoneElse = await SignInAsNewUserAsync(RoleNames.Customer);
        string privateNote = $"Private {Guid.NewGuid():N}";
        await PostBookingAsync(owner, TableP01Id, privateNote);

        string otherPage = await someoneElse.GetStringAsync(MyReservationsUrl);

        Assert.DoesNotContain(privateNote, otherPage);
    }

    // ============================== Helpers ==============================//

    /// <summary>Submits the booking form for 2 guests at 19:00 on <see cref="VisitDate"/>.</summary>
    private static Task<HttpResponseMessage> PostBookingAsync(HttpClient client, int tableId, string? specialRequests)
    {
        Dictionary<string, string> fields = new Dictionary<string, string>
        {
            ["Search.Date"] = VisitDate.ToString("yyyy-MM-dd"),
            ["Search.Time"] = "19:00",
            ["Search.PartySize"] = "2",
            ["DiningTableId"] = tableId.ToString(),
            ["SpecialRequests"] = specialRequests ?? string.Empty
        };

        return FormHelper.PostFormAsync(client, BookUrl, BookUrl, fields);
    }

    /// <summary>Creates a new user with the role and returns a client signed in as them.</summary>
    private async Task<HttpClient> SignInAsNewUserAsync(string roleName)
    {
        HttpClient client = _factory.CreateSecureClient();

        if (roleName == RoleNames.Administrator)
        {
            await FormHelper.LoginAsync(client, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
            return client;
        }

        string email = UniqueEmail(roleName);
        await _factory.CreateUserAsync(email, Password, roleName);
        await FormHelper.LoginAsync(client, email, Password);
        return client;
    }

    /// <summary>A fresh email so users created by different tests never clash.</summary>
    private static string UniqueEmail(string roleName)
    {
        return $"{roleName.ToLowerInvariant()}-{Guid.NewGuid():N}@threegrills.test";
    }

    /// <summary>True when the user's inbox has a notification with this title.</summary>
    private async Task<bool> HasNotificationAsync(string userId, string title)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Notifications.AnyAsync(n => n.UserId == userId && n.Title == title);
    }
}
