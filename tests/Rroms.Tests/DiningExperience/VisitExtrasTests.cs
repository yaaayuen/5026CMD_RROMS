using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rroms.Tests.Menu;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Options;
using Rroms.Web.Services;
namespace Rroms.Tests.DiningExperience;

/// <summary>Part 17: calling staff and asking for the bill, dish ratings, the feedback CSV and recurring announcements.</summary>
public class VisitExtrasTests : IDisposable
{
    private readonly SqliteConnection _connection = new SqliteConnection("DataSource=:memory:");
    private readonly ApplicationDbContext _db;
    private readonly Notices _notices = new Notices();
    private readonly FeedbackWordFilter _words = new FeedbackWordFilter(Microsoft.Extensions.Options.Options.Create(new FeedbackOptions()));
    public VisitExtrasTests()
    {
        _connection.Open();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.Users.Add(new ApplicationUser { Id = "alice", UserName = "alice@test.local", FullName = "Alice Tan" });
        _db.Users.Add(new ApplicationUser { Id = "bob", UserName = "bob@test.local", FullName = "Bob Lee" });
        _db.SaveChanges();
    }
    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    private async Task<Reservation> Visit(string customerId, ReservationStatus status)
    {
        DateTime start = DateTime.UtcNow.AddHours(-1);
        var visit = new Reservation { CustomerId = customerId, DiningTableId = 1, PartySize = 2, Status = status, StartUtc = start, EndUtc = start.AddHours(2) };
        _db.Reservations.Add(visit);
        await _db.SaveChangesAsync();
        return visit;
    }

    private async Task<MenuItem> Dish(string name)
    {
        var dish = new MenuItem { Name = name, Description = "d", MenuCategoryId = OrderTestData.GrillsId, Price = 20m,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        _db.MenuItems.Add(dish);
        await _db.SaveChangesAsync();
        return dish;
    }

    private async Task Order(Reservation visit, OrderStatus status, params MenuItem[] dishes)
    {
        var order = new Order { ReservationId = visit.Id, CustomerId = visit.CustomerId, Status = status, PlacedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        foreach (var dish in dishes) { order.Items.Add(new OrderItem { MenuItemId = dish.Id, Quantity = 1, UnitPrice = 20m }); }
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task OnlyASeatedGuestCanCallStaffAndStaffAreToldOncePerOpenRequest()
    {
        var service = new TableRequestService(_db, _notices);
        Reservation seated = await Visit("alice", ReservationStatus.Seated);
        Reservation booked = await Visit("alice", ReservationStatus.Confirmed);

        Assert.False((await service.RequestAsync("alice", booked.Id, TableRequestKind.CallStaff)).Succeeded);
        Assert.False((await service.RequestAsync("bob", seated.Id, TableRequestKind.CallStaff)).Succeeded);
        Assert.False((await service.RequestAsync("alice", seated.Id, (TableRequestKind)9)).Succeeded);
        Assert.True((await service.RequestAsync("alice", seated.Id, TableRequestKind.CallStaff)).Succeeded);
        Assert.True((await service.RequestAsync("alice", seated.Id, TableRequestKind.CallStaff)).Succeeded);
        Assert.True((await service.RequestAsync("alice", seated.Id, TableRequestKind.Bill)).Succeeded);

        Assert.Equal(new[] { RoleNames.Staff, RoleNames.Staff }, _notices.Roles);
        Assert.Contains($"/Staff/Billing/Visit/{seated.Id}", _notices.Links);
        Assert.Contains(_notices.Titles, t => t.EndsWith("asked for the bill"));
        var open = await service.GetOpenAsync();
        Assert.Equal(new[] { TableRequestKind.CallStaff, TableRequestKind.Bill }, open.Select(r => r.Kind));
        Assert.Equal("Alice Tan", open[0].Reservation!.Customer!.FullName);
        Assert.Equal(2, (await service.GetOpenForCustomerAsync("alice")).Count);
        Assert.Empty(await service.GetOpenForCustomerAsync("bob"));

        Assert.True((await service.HandleAsync(open[0].Id)).Succeeded);
        Assert.False((await service.HandleAsync(open[0].Id)).Succeeded);
        // Asking again after staff came starts a new request.
        Assert.True((await service.RequestAsync("alice", seated.Id, TableRequestKind.CallStaff)).Succeeded);
        Assert.Equal(3, _notices.Roles.Count);
        // Once the guests leave, their requests drop off the kitchen page.
        await _db.Reservations.Where(r => r.Id == seated.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.Completed));
        Assert.Empty(await service.GetOpenAsync());
    }

    [Fact]
    public async Task GuestsRateOnlyDishesTheyWereServedAndAdminsSeeTheAverages()
    {
        var service = new FeedbackService(_db, _notices, _words);
        MenuItem lamb = await Dish("Extras lamb");
        MenuItem ribeye = await Dish("Extras ribeye");
        MenuItem soup = await Dish("Extras soup");
        Reservation first = await Visit("alice", ReservationStatus.Completed);
        Reservation second = await Visit("bob", ReservationStatus.Completed);
        await Order(first, OrderStatus.Completed, lamb, ribeye);
        await Order(first, OrderStatus.Cancelled, soup);
        await Order(second, OrderStatus.Completed, lamb);

        Assert.Equal(new[] { "Extras lamb", "Extras ribeye" }, (await service.GetVisitDishesAsync("alice", first.Id)).Select(m => m.Name));
        Assert.Empty(await service.GetVisitDishesAsync("bob", first.Id));
        Assert.False((await service.SubmitAsync("alice", first.Id, 4, "", new Dictionary<int, int> { [soup.Id] = 5 })).Succeeded);
        Assert.False((await service.SubmitAsync("alice", first.Id, 4, "", new Dictionary<int, int> { [lamb.Id] = 6 })).Succeeded);
        Assert.True((await service.SubmitAsync("alice", first.Id, 4, "Great", new Dictionary<int, int> { [lamb.Id] = 5, [ribeye.Id] = 2 })).Succeeded);
        Assert.True((await service.SubmitAsync("bob", second.Id, 3, "", new Dictionary<int, int> { [lamb.Id] = 4 })).Succeeded);

        var dishes = await service.GetDishSummaryAsync();
        Assert.Equal(new[] { "Extras lamb", "Extras ribeye" }, dishes.Select(d => d.Name));
        Assert.Equal((2, 4.5), (dishes[0].Count, dishes[0].Average));
        var mine = Assert.Single(await service.GetMineAsync("alice"));
        Assert.Equal(2, mine.DishRatings.Count);
        Assert.Equal("Extras ribeye", mine.DishRatings.Single(d => d.Rating == 2).MenuItem!.Name);
        Assert.Equal(3, (await service.GetAsync(null, null, null)).SelectMany(f => f.DishRatings).Count());
    }

    [Fact]
    public async Task CsvKeepsTheFilteredRowsAndCannotRunFormulas()
    {
        var service = new FeedbackService(_db, _notices, _words);
        MenuItem lamb = await Dish("Csv lamb");
        Reservation first = await Visit("alice", ReservationStatus.Completed);
        Reservation second = await Visit("bob", ReservationStatus.Completed);
        await Order(first, OrderStatus.Completed, lamb);
        Assert.True((await service.SubmitAsync("alice", first.Id, 5, "=HYPERLINK(\"x\") \"best\" lamb", new Dictionary<int, int> { [lamb.Id] = 5 })).Succeeded);
        Assert.True((await service.SubmitAsync("bob", second.Id, 1, "Cold food")).Succeeded);

        byte[] bytes = FeedbackCsv.Write(await service.GetAsync(5, null, null));
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3));
        string[] lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("\"Date (Malaysia time)\",\"Reservation\"", lines[0]);
        Assert.Contains("\"Alice Tan\",\"5\",\"'=HYPERLINK(\"\"x\"\") \"\"best\"\" lamb\",\"Yes\",\"No\"", lines[1]);
        Assert.EndsWith("\"Csv lamb 5/5\"", lines[1]);
        Assert.DoesNotContain("Cold food", lines[1]);
        Assert.Equal("\"'@cmd\"", FeedbackCsv.Cell("@cmd"));
        Assert.Equal("\"\"", FeedbackCsv.Cell(null));
    }

    // 2026-10-26 is a Monday. 03:00 UTC is 11:00 in Malaysia.
    private static readonly DateTime Monday11 = new DateTime(2026, 10, 26, 3, 0, 0, DateTimeKind.Utc);

    private static Announcement LunchSet() => new Announcement
    {
        Title = "Lunch set", Message = "m", IsPromotion = true, IsPublished = true, RepeatDays = DiscountDays.Weekdays,
        DailyFromLocal = new TimeOnly(11, 0), DailyUntilLocal = new TimeOnly(14, 0),
        StartUtc = Monday11.AddDays(-7), EndUtc = Monday11.AddDays(30)
    };

    [Fact]
    public void RecurringPostsShowOnlyOnChosenDaysAndHours()
    {
        var lunch = LunchSet();
        Assert.True(AnnouncementSchedule.IsShowing(lunch, Monday11.AddMinutes(30)));
        Assert.Equal(Monday11, AnnouncementSchedule.CurrentStartUtc(lunch, Monday11.AddHours(2)));
        Assert.False(AnnouncementSchedule.IsShowing(lunch, Monday11.AddHours(3)));
        Assert.False(AnnouncementSchedule.IsShowing(lunch, Monday11.AddMinutes(-1)));
        // Saturday at 11:30 is outside Monday to Friday.
        Assert.False(AnnouncementSchedule.IsShowing(lunch, Monday11.AddDays(5).AddMinutes(30)));
        Assert.Equal(Monday11.AddDays(1), AnnouncementSchedule.NextStartUtc(lunch, Monday11.AddHours(1)));
        Assert.Equal(Monday11.AddDays(7), AnnouncementSchedule.NextStartUtc(lunch, Monday11.AddDays(4).AddHours(4)));
        Assert.Equal("Mon–Fri, 11:00 AM – 2:00 PM", AnnouncementSchedule.Describe(lunch));
        lunch.DailyFromLocal = null; lunch.DailyUntilLocal = null;
        Assert.Equal("Mon–Fri, all day", AnnouncementSchedule.Describe(lunch));
        Assert.True(AnnouncementSchedule.IsShowing(lunch, Monday11.AddHours(12)));
        Assert.Equal("", AnnouncementSchedule.Describe(new Announcement()));
        lunch.EndUtc = Monday11.AddHours(1);
        Assert.Null(AnnouncementSchedule.NextStartUtc(lunch, Monday11));
    }

    [Fact]
    public async Task RecurringPromotionIsValidatedAndAnnouncedOncePerDay()
    {
        var service = new AnnouncementService(_db, _notices);
        DateTime now = DateTime.UtcNow;
        DayOfWeek today = MalaysiaTime.FromUtc(now).DayOfWeek;
        Announcement Post(int days) => new Announcement { Title = "Daily deal", Message = "m", IsPromotion = true, IsPublished = true,
            RepeatDays = days, StartUtc = now.AddDays(-1), EndUtc = now.AddDays(14) };

        var oneTime = Post(DiscountDays.Bit(today));
        oneTime.DailyFromLocal = new TimeOnly(11, 0);
        Assert.False((await service.SaveAsync(oneTime)).Succeeded);
        var backwards = Post(DiscountDays.Bit(today));
        backwards.DailyFromLocal = new TimeOnly(14, 0); backwards.DailyUntilLocal = new TimeOnly(11, 0);
        Assert.False((await service.SaveAsync(backwards)).Succeeded);
        var neverToday = Post(DiscountDays.Bit(today));
        neverToday.StartUtc = now.AddHours(1); neverToday.EndUtc = now.AddDays(2);
        neverToday.RepeatDays = DiscountDays.Bit((DayOfWeek)(((int)today + 4) % 7));
        Assert.False((await service.SaveAsync(neverToday)).Succeeded);
        Assert.False((await service.SaveAsync(Post(200))).Succeeded);

        // All day, every day except tomorrow: showing now, so customers hear about it straight away.
        int allButTomorrow = 127 & ~DiscountDays.Bit((DayOfWeek)(((int)today + 1) % 7));
        Assert.True((await service.SaveAsync(Post(allButTomorrow))).Succeeded);
        Assert.Single(_notices.Roles);
        Assert.Single(await service.GetAsync(true));
        Assert.Equal(0, await service.NotifyDueAsync());
        // The next day it shows, the notice goes out again, once.
        await _db.Announcements.ExecuteUpdateAsync(s => s.SetProperty(a => a.NotifiedAtUtc, now.AddDays(-1)));
        Assert.Equal(1, await service.NotifyDueAsync());
        Assert.Equal(0, await service.NotifyDueAsync());
        Assert.Equal(2, _notices.Roles.Count);
        // On a day it does not repeat on, it is hidden from customers.
        await _db.Announcements.ExecuteUpdateAsync(s => s.SetProperty(a => a.RepeatDays, DiscountDays.Bit((DayOfWeek)(((int)today + 1) % 7))));
        Assert.Empty(await service.GetAsync(true));
        Assert.Equal(0, await service.NotifyDueAsync());
    }

    private class Notices : INotificationService
    {
        public List<string> Roles { get; } = new List<string>();
        public List<string> Titles { get; } = new List<string>();
        public List<string?> Links { get; } = new List<string?>();
        public Task NotifyUserAsync(string userId, NotificationType type, string title, string message, string? linkUrl = null)
        { Titles.Add(title); Links.Add(linkUrl); return Task.CompletedTask; }
        public Task NotifyRoleAsync(string roleName, NotificationType type, string title, string message, string? linkUrl = null)
        { Roles.Add(roleName); Titles.Add(title); Links.Add(linkUrl); return Task.CompletedTask; }
    }
}
