using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.Reservations;

/// <summary>
/// WL-3 staff "modify" (brief: staff confirm, modify or cancel customer reservations): the Edit page
/// changes time, party size and table through ModifyByStaffAsync. Uses tables T05, P01 and P02 so it never
/// meets the other tests in this class (they use T01-T04 at 12:00).
/// </summary>
public partial class StaffReservationPagesTests
{
    private const int TableT05Id = 5;
    private const int TableP01Id = 7;
    private const int TableP02Id = 8;

    [Fact]
    public async Task Edit_ConfirmedBooking_HasEditButtonAndFormStartsWithCurrentValues()
    {
        (HttpClient staff, string _) = await SignInAsNewUserAsync(RoleNames.Staff);
        (HttpClient _, string customerId) = await SignInAsNewUserAsync(RoleNames.Customer);
        int id = await BookAsync(customerId, TableT05Id, new TimeOnly(14, 0));

        string day = await staff.GetStringAsync(DayUrl);
        string form = await staff.GetStringAsync($"{PageUrl}/Edit/{id}");

        Assert.Contains($"href=\"/Staff/Reservations/Edit/{id}\"", RowOf(day, id));
        Assert.Contains("Current table", form);
        Assert.Contains("value=\"14:00\" selected", form);
    }

    [Fact]
    public async Task Edit_StaffMoveTimePartyAndTable_SavedConfirmedAndCustomerNotified()
    {
        (HttpClient staff, string _) = await SignInAsNewUserAsync(RoleNames.Staff);
        (HttpClient _, string customerId) = await SignInAsNewUserAsync(RoleNames.Customer);
        int id = await BookAsync(customerId, TableP01Id, new TimeOnly(15, 0));
        string url = $"{PageUrl}/Edit/{id}";

        HttpResponseMessage response = await FormHelper.PostFormAsync(staff, url, url, new Dictionary<string, string>
        {
            ["Search.Date"] = VisitDate.ToString("yyyy-MM-dd"),
            ["Search.Time"] = "16:00",
            ["Search.PartySize"] = "3",
            ["DiningTableId"] = TableP02Id.ToString(),
            ["SpecialRequests"] = "Moved by staff on the phone"
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Staff/Reservations", response.Headers.Location?.ToString());
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Reservation saved = await db.Reservations.Include(r => r.Slots).SingleAsync(r => r.Id == id);
        Assert.Equal((TableP02Id, 3, ReservationStatus.Confirmed), (saved.DiningTableId, saved.PartySize, saved.Status));
        Assert.Equal(new TimeOnly(16, 0), TimeOnly.FromDateTime(Rroms.Web.Common.MalaysiaTime.FromUtc(saved.StartUtc)));
        Assert.All(saved.Slots, s => Assert.Equal(TableP02Id, s.DiningTableId));
        Assert.True(await db.Notifications.AnyAsync(n => n.UserId == customerId && n.Title == "Reservation updated"));
    }

    [Fact]
    public async Task Edit_OntoATakenTable_IsRefusedAndUnchanged()
    {
        (HttpClient staff, string _) = await SignInAsNewUserAsync(RoleNames.Staff);
        (HttpClient _, string customerId) = await SignInAsNewUserAsync(RoleNames.Customer);
        int taken = await BookAsync(customerId, TableT05Id, new TimeOnly(18, 0));
        int id = await BookAsync(customerId, TableP01Id, new TimeOnly(18, 0));
        string url = $"{PageUrl}/Edit/{id}";

        HttpResponseMessage response = await FormHelper.PostFormAsync(staff, url, url, new Dictionary<string, string>
        {
            ["Search.Date"] = VisitDate.ToString("yyyy-MM-dd"),
            ["Search.Time"] = "18:00",
            ["Search.PartySize"] = "2",
            ["DiningTableId"] = TableT05Id.ToString()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("alert-danger", await response.Content.ReadAsStringAsync());
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(TableP01Id, await db.Reservations.Where(r => r.Id == id).Select(r => r.DiningTableId).SingleAsync());
        Assert.NotEqual(0, taken);
    }

    [Fact]
    public async Task Edit_SeatedBooking_CannotBeEdited()
    {
        (HttpClient staff, string _) = await SignInAsNewUserAsync(RoleNames.Staff);
        (HttpClient _, string customerId) = await SignInAsNewUserAsync(RoleNames.Customer);
        int id = await BookAsync(customerId, TableP02Id, new TimeOnly(11, 0));
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Reservations.Where(r => r.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.Seated));
        }

        string form = await staff.GetStringAsync($"{PageUrl}/Edit/{id}");
        string day = await staff.GetStringAsync(DayUrl);

        Assert.Contains("booking cannot be edited", form);
        Assert.DoesNotContain("name=\"DiningTableId\"", form);
        Assert.DoesNotContain($"/Staff/Reservations/Edit/{id}\"", RowOf(day, id));
    }

    [Fact]
    public async Task Edit_CustomerIsDenied()
    {
        (HttpClient customer, string _) = await SignInAsNewUserAsync(RoleNames.Customer);

        HttpResponseMessage response = await customer.GetAsync($"{PageUrl}/Edit/1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }
}
