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
/// WL-3 acceptance for the staff daily reservations page: who can open it, the day's list,
/// the buttons each status offers, and that a button really changes the booking.
/// </summary>
/// <remarks>
/// Integration tests on the running app. All tests share one database, so each test books its own table and time.
/// The date shown is one week ahead, the same day the bookings are made for.
/// </remarks>
public class StaffReservationPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";
    private const string PageUrl = "/Staff/Reservations";

    private const int TableT01Id = 1;
    private const int TableT02Id = 2;
    private const int TableT03Id = 3;
    private const int TableT04Id = 4;

    /// <summary>A visit one week ahead (Malaysia), always in the future and inside the booking window.</summary>
    private static readonly DateOnly VisitDate = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow)).AddDays(7);

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public StaffReservationPagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The page for the test day, e.g. /Staff/Reservations?Date=2026-10-23.</summary>
    private static string DayUrl => $"{PageUrl}?Date={VisitDate:yyyy-MM-dd}";

    // ============================== Access ==============================//

    /// <summary>Someone who is not signed in is sent to the login page.</summary>
    [Fact]
    public async Task Anonymous_IsSentToLogin()
    {
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await client.GetAsync(PageUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    /// <summary>Customers cannot see other people's bookings or change them.</summary>
    [Fact]
    public async Task Customer_IsDenied()
    {
        (HttpClient client, string _) = await SignInAsNewUserAsync(RoleNames.Customer);

        HttpResponseMessage response = await client.GetAsync(PageUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    /// <summary>The "Today's reservations" tile on the staff home page links here.</summary>
    [Fact]
    public async Task StaffDashboard_LinksToReservations()
    {
        (HttpClient client, string _) = await SignInAsNewUserAsync(RoleNames.Staff);

        string html = await client.GetStringAsync("/Staff");

        Assert.Contains($"href=\"{PageUrl}\"", html);
    }

    // ============================== List and buttons ==============================//

    /// <summary>A Pending booking (made before auto-confirm) is listed with only Confirm and Cancel offered.</summary>
    [Fact]
    public async Task PendingBooking_IsListedWithConfirmAndCancel()
    {
        (HttpClient staff, string _) = await SignInAsNewUserAsync(RoleNames.Staff);
        (HttpClient _, string customerId) = await SignInAsNewUserAsync(RoleNames.Customer);
        int id = await BookAsync(customerId, TableT01Id, new TimeOnly(12, 0));
        await MakePendingAsync(id);

        string html = await staff.GetStringAsync(DayUrl);
        string row = RowOf(html, id);

        Assert.Contains("Pending", row);
        Assert.Contains(">Confirm<", row);
        Assert.Contains(">Cancel<", row);
        Assert.DoesNotContain(">Seat<", row);
    }

    /// <summary>Pressing Confirm really confirms the booking and returns to the same day.</summary>
    [Fact]
    public async Task ConfirmButton_ConfirmsTheBooking()
    {
        (HttpClient staff, string _) = await SignInAsNewUserAsync(RoleNames.Staff);
        (HttpClient _, string customerId) = await SignInAsNewUserAsync(RoleNames.Customer);
        int id = await BookAsync(customerId, TableT02Id, new TimeOnly(12, 0));
        await MakePendingAsync(id);

        HttpResponseMessage response = await PressAsync(staff, id, ReservationStatus.Confirmed);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"Date={VisitDate:yyyy-MM-dd}", response.Headers.Location?.ToString());
        Assert.Equal(ReservationStatus.Confirmed, await StatusOfAsync(id));
    }

    /// <summary>A new booking is already Confirmed, so its row offers Seat, Cancel and Mark absent instead of Confirm.</summary>
    [Fact]
    public async Task ConfirmedBooking_OffersSeatCancelAndNoShow()
    {
        (HttpClient staff, string _) = await SignInAsNewUserAsync(RoleNames.Staff);
        (HttpClient _, string customerId) = await SignInAsNewUserAsync(RoleNames.Customer);
        int id = await BookAsync(customerId, TableT03Id, new TimeOnly(12, 0));

        string row = RowOf(await staff.GetStringAsync(DayUrl), id);

        Assert.Contains(">Seat<", row);
        Assert.Contains(">Cancel<", row);
        Assert.Contains(">Mark absent<", row);
        Assert.DoesNotContain(">Confirm<", row);
    }

    /// <summary>A forged request to seat a Pending booking is refused by the service and changes nothing.</summary>
    [Fact]
    public async Task IllegalMove_IsRefusedAndUnchanged()
    {
        (HttpClient staff, string _) = await SignInAsNewUserAsync(RoleNames.Staff);
        (HttpClient _, string customerId) = await SignInAsNewUserAsync(RoleNames.Customer);
        int id = await BookAsync(customerId, TableT04Id, new TimeOnly(12, 0));
        await MakePendingAsync(id);

        HttpResponseMessage response = await PressAsync(staff, id, ReservationStatus.Seated);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ReservationStatus.Pending, await StatusOfAsync(id));
    }

    // ============================== Helpers ==============================//

    /// <summary>Creates a user with the role, signs them in, and returns the client and their user ID.</summary>
    private async Task<(HttpClient Client, string UserId)> SignInAsNewUserAsync(string roleName)
    {
        string email = $"{roleName.ToLowerInvariant()}-{Guid.NewGuid():N}@threegrills.test";
        ApplicationUser user = await _factory.CreateUserAsync(email, Password, roleName);

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

    /// <summary>Presses a status button on the test day's page, the way the browser would.</summary>
    private static Task<HttpResponseMessage> PressAsync(HttpClient staff, int id, ReservationStatus to)
    {
        return FormHelper.PostFormAsync(
            staff,
            DayUrl,
            $"{PageUrl}?handler=Move&Date={VisitDate:yyyy-MM-dd}",
            new Dictionary<string, string> { ["id"] = id.ToString(), ["to"] = to.ToString() });
    }

    /// <summary>The HTML of the table row that holds the booking's hidden id field.</summary>
    private static string RowOf(string html, int id)
    {
        int idField = html.IndexOf($"name=\"id\" value=\"{id}\"", StringComparison.Ordinal);
        Assert.True(idField >= 0, $"Booking {id} is not on the page.");
        int rowStart = html.LastIndexOf("<tr>", idField, StringComparison.Ordinal);
        int rowEnd = html.IndexOf("</tr>", idField, StringComparison.Ordinal);
        return html.Substring(rowStart, rowEnd - rowStart);
    }

    /// <summary>Turns a booking back into Pending, like one made before bookings were confirmed automatically.</summary>
    private async Task MakePendingAsync(int id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Reservations.Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.Pending));
    }

    /// <summary>The booking's status as stored now.</summary>
    private async Task<ReservationStatus> StatusOfAsync(int id)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.Reservations.Where(r => r.Id == id).Select(r => r.Status).SingleAsync();
    }
}
