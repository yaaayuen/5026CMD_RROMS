using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;

namespace Rroms.Tests.Restaurant;

/// <summary>WL-1: half-hour time lists on Opening hours, and the right button on the public Opening hours page.</summary>
public class HoursAndVisitPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";
    private readonly TestWebApplicationFactory _factory;

    public HoursAndVisitPagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OpeningHours_TimesAreHalfHourListsWithTheSavedTimeSelected()
    {
        HttpClient admin = await SignInAsync(RoleNames.Administrator);

        string html = await admin.GetStringAsync("/Admin/Restaurant/Hours");

        Assert.DoesNotContain("type=\"time\"", html);
        Assert.Contains("<select form=\"day-Monday\" name=\"day.OpensAt\"", html);
        Assert.Contains("<option value=\"11:00\" selected=\"selected\">11:00 AM</option>", html);
        Assert.Contains("<option value=\"23:30\">11:30 PM</option>", html);
        Assert.DoesNotContain("value=\"11:15\"", html);
    }

    [Fact]
    public async Task VisitPage_VisitorGetsBookATable()
    {
        HttpClient visitor = _factory.CreateSecureClient();

        string html = await visitor.GetStringAsync("/Visit");

        Assert.Contains("href=\"/Customer/Reservations/Book\"", html);
    }

    [Theory]
    [InlineData(RoleNames.Staff)]
    [InlineData(RoleNames.Administrator)]
    public async Task VisitPage_StaffAndAdminGetTheirReservationsPageInsteadOfBooking(string role)
    {
        HttpClient employee = await SignInAsync(role);

        string html = await employee.GetStringAsync("/Visit");

        Assert.DoesNotContain("href=\"/Customer/Reservations/Book\"", html);
        Assert.Contains("href=\"/Staff/Reservations\"", html);
        Assert.Contains("Today's reservations", html);
    }

    private async Task<HttpClient> SignInAsync(string role)
    {
        string email = $"hours-{Guid.NewGuid():N}@threegrills.test";
        await _factory.CreateUserAsync(email, Password, role);
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);
        return client;
    }
}
