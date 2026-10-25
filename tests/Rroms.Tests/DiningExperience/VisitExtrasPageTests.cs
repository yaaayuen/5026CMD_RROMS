using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.History;
using Rroms.Tests.Infrastructure;
using Rroms.Tests.Menu;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
namespace Rroms.Tests.DiningExperience;

/// <summary>Part 17 through the real pages: table requests, the rating prompt, dish ratings, the CSV download and recurring posts.</summary>
public class VisitExtrasPageTests
{
    private const string Password = "Test!Customer2026";

    [Fact]
    public async Task SeatedGuestCallsStaffAndAsksForTheBillAndStaffMarkItDone()
    {
        using var factory = new TestWebApplicationFactory();
        ApplicationUser guest = await factory.CreateUserAsync("alex-help@test.local", Password, RoleNames.Customer);
        int visit = await AddVisitAsync(factory, guest.Id, ReservationStatus.Seated);
        using var customer = factory.CreateSecureClient();
        await FormHelper.LoginAsync(customer, "alex-help@test.local", Password);
        string tracking = await customer.GetStringAsync("/Customer/Tracking?handler=Status");
        Assert.Contains("Call staff", tracking);
        Assert.Contains("Request the bill", tracking);

        var asked = await FormHelper.PostFormAsync(customer, "/Customer/Tracking", $"/Customer/Tracking?handler=Request&reservationId={visit}&kind=Bill",
            new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, asked.StatusCode);
        Assert.Contains("Bill requested", await customer.GetStringAsync("/Customer/Tracking?handler=Status"));

        using var staff = factory.CreateSecureClient();
        await FormHelper.LoginAsync(staff, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        string queue = await staff.GetStringAsync("/Staff/Orders?handler=Queue");
        Assert.Contains("Table requests", queue);
        Assert.Contains("asked for the bill", queue);
        Assert.Contains($"/Staff/Billing/Visit/{visit}", queue);
        int requestId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            requestId = (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TableRequests.SingleAsync()).Id;
        }
        var done = await FormHelper.PostFormAsync(staff, "/Staff/Orders", $"/Staff/Orders?handler=HandleRequest&requestId={requestId}",
            new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        Assert.DoesNotContain("Table requests", await staff.GetStringAsync("/Staff/Orders?handler=Queue"));
        Assert.Contains("Request the bill", await customer.GetStringAsync("/Customer/Tracking?handler=Status"));
    }

    [Fact]
    public async Task FinishedVisitPromptsARatingAndTheGuestRatesTheirDishes()
    {
        using var factory = new TestWebApplicationFactory();
        ApplicationUser guest = await factory.CreateUserAsync("alex-rate@test.local", Password, RoleNames.Customer);
        int visit = await AddVisitAsync(factory, guest.Id, ReservationStatus.Completed);
        int dish;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            MenuItem lamb = await OrderTestData.AddDishAsync(db, "Rated lamb chop");
            dish = lamb.Id;
            var order = new Order { ReservationId = visit, CustomerId = guest.Id, Status = OrderStatus.Completed, TotalAmount = 20m,
                PlacedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
            order.Items.Add(new OrderItem { MenuItemId = lamb.Id, Quantity = 1, UnitPrice = 20m });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }
        using var customer = factory.CreateSecureClient();
        await FormHelper.LoginAsync(customer, "alex-rate@test.local", Password);
        string tracking = await customer.GetStringAsync("/Customer/Tracking?handler=Status");
        Assert.Contains("How was your visit on", tracking);
        Assert.Contains($"/Customer/Feedback?reservationId={visit}&amp;rating=4", tracking);
        Assert.DoesNotContain("Call staff", tracking);

        string form = await customer.GetStringAsync($"/Customer/Feedback?reservationId={visit}&rating=4");
        Assert.Contains("Rate your dishes (optional)", form);
        Assert.Contains("Rated lamb chop", form);
        Assert.Matches("<option[^>]*(selected=\"selected\"[^>]*value=\"4\"|value=\"4\"[^>]*selected=\"selected\")[^>]*>4 stars", form);
        var sent = await FormHelper.PostFormAsync(customer, $"/Customer/Feedback?reservationId={visit}", "/Customer/Feedback",
            new Dictionary<string, string> { ["ReservationId"] = visit.ToString(), ["Rating"] = "4", ["Comment"] = "Juicy",
                [$"DishRatings[{dish}]"] = "5" });
        Assert.Equal(HttpStatusCode.Redirect, sent.StatusCode);
        Assert.Contains("Rated lamb chop 5★", WebUtility.HtmlDecode(await customer.GetStringAsync("/Customer/Feedback")));
        Assert.DoesNotContain("How was your visit on", await customer.GetStringAsync("/Customer/Tracking?handler=Status"));

        using var admin = factory.CreateSecureClient();
        await FormHelper.LoginAsync(admin, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        string page = await admin.GetStringAsync("/Admin/Feedback");
        Assert.Contains("Dish ratings", page);
        Assert.Contains("<td>Rated lamb chop</td><td>5.0 ★</td><td>1</td>", page);
    }

    [Fact]
    public async Task AdminDownloadsTheFilteredFeedbackAsCsv()
    {
        using var factory = new TestWebApplicationFactory();
        ApplicationUser guest = await factory.CreateUserAsync("alex-csv@test.local", Password, RoleNames.Customer);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var (stars, text) in new[] { (5, "Loved the csv steak"), (2, "Slow csv service") })
            {
                Reservation visit = await HistoryTestData.AddReservationAsync(db, guest.Id, DateTime.UtcNow.AddDays(-2), ReservationStatus.Completed);
                db.Feedback.Add(new Feedback { ReservationId = visit.Id, Rating = stars, Comment = text, CreatedAtUtc = DateTime.UtcNow });
            }
            await db.SaveChangesAsync();
        }
        using var admin = factory.CreateSecureClient();
        await FormHelper.LoginAsync(admin, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        // The download link keeps the filters on screen.
        Assert.Matches("href=\"/Admin/Feedback\\?(?=[^\"]*handler=Export)(?=[^\"]*Rating=2)", await admin.GetStringAsync("/Admin/Feedback?Rating=2"));

        var file = await admin.GetAsync("/Admin/Feedback?handler=Export&Rating=2");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("text/csv", file.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("three-grills-feedback-", file.Content.Headers.ContentDisposition!.FileNameStar ?? file.Content.Headers.ContentDisposition.FileName);
        string csv = await file.Content.ReadAsStringAsync();
        Assert.Contains("Slow csv service", csv);
        Assert.DoesNotContain("Loved the csv steak", csv);

        using var customer = factory.CreateSecureClient();
        await FormHelper.LoginAsync(customer, "alex-csv@test.local", Password);
        var denied = await customer.GetAsync("/Admin/Feedback?handler=Export");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("AccessDenied", denied.Headers.Location!.ToString());
    }

    [Fact]
    public async Task AdminSavesARecurringPostWithDaysAndHours()
    {
        using var factory = new TestWebApplicationFactory();
        using var admin = factory.CreateSecureClient();
        await FormHelper.LoginAsync(admin, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        Assert.Contains("Repeat on (optional)", await admin.GetStringAsync("/Admin/Announcements"));
        DateTime start = MalaysiaTime.FromUtc(DateTime.UtcNow).Date;
        var saved = await FormHelper.PostFormAsync(admin, "/Admin/Announcements", "/Admin/Announcements", new Dictionary<string, string>
        {
            ["Input.Title"] = "Monday lunch set", ["Input.Message"] = "Two courses", ["Input.IsPublished"] = "true",
            ["StartLocal"] = start.ToString("yyyy-MM-ddTHH:mm"), ["EndLocal"] = start.AddDays(14).ToString("yyyy-MM-ddTHH:mm"),
            ["RepeatOn"] = "Monday", ["Input.DailyFromLocal"] = "11:00", ["Input.DailyUntilLocal"] = "14:00"
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("Repeats Mon, 11:00 AM", await admin.GetStringAsync("/Admin/Announcements"));
        using IServiceScope scope = factory.Services.CreateScope();
        var post = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Announcements.SingleAsync(a => a.Title == "Monday lunch set");
        Assert.Equal(1 << (int)DayOfWeek.Monday, post.RepeatDays);
        Assert.Equal(new TimeOnly(14, 0), post.DailyUntilLocal);
    }

    private static async Task<int> AddVisitAsync(TestWebApplicationFactory factory, string customerId, ReservationStatus status)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Reservation visit = await HistoryTestData.AddReservationAsync(db, customerId, DateTime.UtcNow.AddHours(-1), status);
        return visit.Id;
    }
}
