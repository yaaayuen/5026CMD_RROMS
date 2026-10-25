using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Rroms.Tests.Menu;
using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Options;
using Rroms.Web.Services;
namespace Rroms.Tests.DiningExperience;

/// <summary>Part 16: pausing the kitchen, finishing early, adding time, kitchen statistics and chatbot topic counts.</summary>
public class KitchenControlsTests : IDisposable
{
    private readonly SqliteConnection _connection = new SqliteConnection("DataSource=:memory:");
    private readonly ApplicationDbContext _db;
    private readonly Notices _notices = new Notices();
    private readonly OrderProcessingService _kitchen;
    private readonly KitchenInsightsService _insights;

    public KitchenControlsTests()
    {
        _connection.Open();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.Users.Add(new ApplicationUser { Id = "alice", UserName = "alice@test.local", FullName = "Alice" });
        _db.SaveChanges();
        _kitchen = new OrderProcessingService(_db, _notices, Microsoft.Extensions.Options.Options.Create(new KitchenOptions { CookingSlots = 2 }));
        _insights = new KitchenInsightsService(_db, NullLogger<KitchenInsightsService>.Instance);
    }
    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    private async Task<MenuItem> Dish(string name, int? minutes)
    {
        var dish = new MenuItem { Name = name, Description = "d", MenuCategoryId = OrderTestData.GrillsId, Price = 20m, CookingMinutes = minutes,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        _db.MenuItems.Add(dish);
        await _db.SaveChangesAsync();
        return dish;
    }

    private async Task<Order> SeatedOrder(int placedMinutesAgo, params MenuItem[] dishes)
    {
        DateTime start = DateTime.UtcNow.AddHours(-2);
        var order = new Order
        {
            Reservation = new Reservation { CustomerId = "alice", DiningTableId = 1, PartySize = 2, Status = ReservationStatus.Seated, StartUtc = start, EndUtc = start.AddHours(3) },
            CustomerId = "alice", Status = OrderStatus.Placed, PlacedAtUtc = DateTime.UtcNow.AddMinutes(-placedMinutesAgo), UpdatedAtUtc = DateTime.UtcNow
        };
        foreach (MenuItem dish in dishes) { order.Items.Add(new OrderItem { MenuItemId = dish.Id, Quantity = 1, UnitPrice = 20m }); }
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return order;
    }

    private async Task<OrderStatus> StatusOf(int id) { _db.ChangeTracker.Clear(); return (await _db.Orders.FindAsync(id))!.Status; }

    [Fact]
    public async Task PausedKitchenFinishesCookingButStartsNothingNewExceptDrinks()
    {
        MenuItem steak = await Dish("Pause steak", 10);
        MenuItem cola = await Dish("Pause cola", null);
        Order cooking = await SeatedOrder(30, steak);
        await _kitchen.AdvanceKitchenAsync();
        await _kitchen.SetPausedAsync(true);
        Assert.True(await _kitchen.IsPausedAsync());
        Order waiting = await SeatedOrder(20, steak);
        Order drinks = await SeatedOrder(10, cola);
        await _db.KitchenTickets.Where(t => t.OrderId == cooking.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ReadyAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        await _kitchen.AdvanceKitchenAsync();

        Assert.Equal(OrderStatus.ReadyToServe, await StatusOf(cooking.Id));
        Assert.Equal(OrderStatus.Placed, await StatusOf(waiting.Id));
        Assert.Equal(OrderStatus.ReadyToServe, await StatusOf(drinks.Id));
        Assert.True((await _kitchen.GetKitchenBoardAsync()).IsPaused);

        await _kitchen.SetPausedAsync(false);
        await _kitchen.AdvanceKitchenAsync();
        Assert.Equal(OrderStatus.Preparing, await StatusOf(waiting.Id));
    }

    [Fact]
    public async Task StaffCanFinishEarlyOrAddTimeOnlyWhileCooking()
    {
        MenuItem lamb = await Dish("Control lamb", 15);
        Order first = await SeatedOrder(30, lamb);
        Order second = await SeatedOrder(20, lamb);
        await _kitchen.AdvanceKitchenAsync();
        var before = await _db.KitchenTickets.AsNoTracking().SingleAsync(t => t.OrderId == second.Id);

        Assert.False((await _kitchen.AddCookingTimeAsync(second.Id, 0)).Succeeded);
        Assert.False((await _kitchen.AddCookingTimeAsync(second.Id, OrderProcessingService.MaxExtraMinutes + 1)).Succeeded);
        Assert.True((await _kitchen.AddCookingTimeAsync(second.Id, 5)).Succeeded);
        var after = await _db.KitchenTickets.AsNoTracking().SingleAsync(t => t.OrderId == second.Id);
        // SQLite date maths rounds to the millisecond, so allow a moment either way.
        Assert.InRange((after.ReadyAtUtc - before.ReadyAtUtc).TotalSeconds, 299, 301);
        Assert.Equal(20, after.CookingMinutes);
        Assert.Contains("A little more time", _notices.Titles);

        Assert.True((await _kitchen.FinishEarlyAsync(first.Id)).Succeeded);
        Assert.Equal(OrderStatus.ReadyToServe, await StatusOf(first.Id));
        var finished = await _db.KitchenTickets.AsNoTracking().SingleAsync(t => t.OrderId == first.Id);
        Assert.True(finished.ReadyAtUtc <= DateTime.UtcNow);
        // A second press, or adding time to food that is already ready, is refused.
        Assert.False((await _kitchen.FinishEarlyAsync(first.Id)).Succeeded);
        Assert.False((await _kitchen.AddCookingTimeAsync(first.Id, 5)).Succeeded);
    }

    [Fact]
    public async Task StatisticsAverageWaitingCookingAndServing()
    {
        MenuItem pasta = await Dish("Stats pasta", 10);
        MenuItem tea = await Dish("Stats tea", null);
        Order cooked = await SeatedOrder(12, pasta);
        await SeatedOrder(5, tea);
        await _kitchen.AdvanceKitchenAsync();
        // Pretend the pasta joined the line 12 minutes ago, cooked for 10 and waited 4 at the pass.
        DateTime started = DateTime.UtcNow.AddMinutes(-14);
        await _db.KitchenTickets.Where(t => t.OrderId == cooked.Id).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.QueuedSinceUtc, started.AddMinutes(-12)).SetProperty(t => t.StartedAtUtc, started)
            .SetProperty(t => t.ReadyAtUtc, started.AddMinutes(10)));
        await _kitchen.AdvanceKitchenAsync();
        Assert.True((await _kitchen.ChangeStatusAsync(cooked.Id, OrderStatus.ReadyToServe, OrderStatus.Served)).Succeeded);

        DateOnly today = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
        var stats = await _insights.GetKitchenStatsAsync(today);
        Assert.Equal(2, stats.Orders);
        Assert.Equal(1, stats.DrinksOnly);
        Assert.Equal(12, stats.AverageWaitMinutes!.Value, 1);
        Assert.Equal(10, stats.AverageCookMinutes!.Value, 1);
        Assert.Equal(4, stats.AverageReadyToServedMinutes!.Value, 1);
        Assert.NotNull(stats.BusiestHour);
        Assert.Equal(2, stats.OrdersByHour.Sum());
        Assert.Equal(7, stats.Week.Count);
        Assert.Equal(today, stats.Week[^1].Day);
        Assert.Equal(2, stats.Week[^1].Orders);
        Assert.Equal(0, (await _insights.GetKitchenStatsAsync(today.AddDays(-30))).Orders);
    }

    [Fact]
    public async Task ChatInsightsCountTopicsAndSourcesOnly()
    {
        await _insights.RecordQuestionAsync("menu", "Local");
        await _insights.RecordQuestionAsync("menu", "Local");
        await _insights.RecordQuestionAsync("other", "AI");
        await _insights.RecordQuestionAsync("other", "Fallback");
        _db.ChatQuestionLogs.Add(new ChatQuestionLog { Topic = "hours", Source = "Local", AskedAtUtc = DateTime.UtcNow.AddDays(-40) });
        await _db.SaveChangesAsync();

        var month = await _insights.GetChatInsightsAsync(30);
        Assert.Equal(4, month.Total);
        Assert.Equal((2, 1, 1), (month.Local, month.Ai, month.Fallback));
        Assert.Equal(("menu", 2), month.Topics[0]);
        Assert.Equal(30, month.PerDay.Count);
        Assert.Equal(4, month.PerDay[^1].Count);
        Assert.Equal(5, (await _insights.GetChatInsightsAsync(90)).Total);
        // The log has no column for the question or the person asking.
        Assert.DoesNotContain(typeof(ChatQuestionLog).GetProperties(), p => p.Name is "Question" or "Text" or "UserId" or "CustomerId");
    }

    private class Notices : INotificationService
    {
        public List<string> Titles { get; } = new List<string>();
        public Task NotifyUserAsync(string userId, NotificationType type, string title, string message, string? linkUrl = null)
        { Titles.Add(title); return Task.CompletedTask; }
        public Task NotifyRoleAsync(string roleName, NotificationType type, string title, string message, string? linkUrl = null)
        { Titles.Add(title); return Task.CompletedTask; }
    }
}
