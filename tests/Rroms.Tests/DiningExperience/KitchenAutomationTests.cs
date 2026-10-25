using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rroms.Tests.Menu;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
using Rroms.Web.Services;
namespace Rroms.Tests.DiningExperience;

/// <summary>Part 15: the automatic kitchen with four cooking slots, a queue with estimates and dish cooking times.</summary>
public class KitchenAutomationTests : IDisposable
{
    private readonly SqliteConnection _connection = new SqliteConnection("DataSource=:memory:");
    private readonly ApplicationDbContext _db;
    private readonly Notices _notices = new Notices();
    private readonly OrderProcessingService _kitchen;
    private int _tableGuests;

    public KitchenAutomationTests()
    {
        _connection.Open();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.Users.Add(new ApplicationUser { Id = "alice", UserName = "alice@test.local", FullName = "Alice" });
        _db.SaveChanges();
        _kitchen = new OrderProcessingService(_db, _notices, Microsoft.Extensions.Options.Options.Create(new KitchenOptions { CookingSlots = 4 }));
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

    /// <summary>A visit (started an hour ago) with one order of the given dishes, placed the given minutes ago.</summary>
    private Task<Order> Order(ReservationStatus visit, int placedMinutesAgo, params MenuItem[] dishes) =>
        Order(visit, placedMinutesAgo, 60, dishes);

    private async Task<Order> Order(ReservationStatus visit, int placedMinutesAgo, int visitStartedMinutesAgo, params MenuItem[] dishes)
    {
        DateTime start = DateTime.UtcNow.AddMinutes(-visitStartedMinutesAgo);
        var reservation = new Reservation { CustomerId = "alice", DiningTableId = 1, PartySize = 2 + _tableGuests++ % 3,
            Status = visit, StartUtc = start, EndUtc = start.AddHours(2) };
        var order = new Order { Reservation = reservation, CustomerId = "alice", Status = OrderStatus.Placed,
            PlacedAtUtc = DateTime.UtcNow.AddMinutes(-placedMinutesAgo), UpdatedAtUtc = DateTime.UtcNow };
        foreach (MenuItem dish in dishes) { order.Items.Add(new OrderItem { MenuItemId = dish.Id, Quantity = 1, UnitPrice = 20m }); }
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return order;
    }

    private async Task<OrderStatus> StatusOf(int id) { _db.ChangeTracker.Clear(); return (await _db.Orders.FindAsync(id))!.Status; }

    /// <summary>Moves a countdown into the past, as if its cooking time had passed.</summary>
    private Task FinishCooking(int orderId) =>
        _db.KitchenTickets.Where(t => t.OrderId == orderId).ExecuteUpdateAsync(s => s.SetProperty(t => t.ReadyAtUtc, DateTime.UtcNow.AddSeconds(-1)));

    [Fact]
    public async Task AtMostFourOrdersCookAndTheRestWaitInLineWithEstimates()
    {
        MenuItem steak = await Dish("Kitchen steak", 10);
        var orders = new List<Order>();
        for (int n = 0; n < 6; n++) { orders.Add(await Order(ReservationStatus.Seated, 30 - n, steak)); }
        Assert.Equal(4, await _kitchen.AdvanceKitchenAsync());
        Assert.Equal(0, await _kitchen.AdvanceKitchenAsync());

        KitchenBoard board = await _kitchen.GetKitchenBoardAsync();
        Assert.Equal(4, board.Cooking.Count);
        Assert.Equal(new[] { orders[4].Id, orders[5].Id }, board.Queue.Select(e => e.Order.Id));
        Assert.Equal(new[] { 1, 2 }, board.Queue.Select(e => e.QueuePosition));
        // Both queued orders take the first slots to free up, ten minutes from now, and cook for ten minutes.
        DateTime firstFree = board.Cooking.Min(e => e.ReadyAtUtc!.Value);
        Assert.All(board.Queue, e => Assert.Equal(firstFree, e.StartUtc));
        Assert.All(board.Queue, e => Assert.Equal(firstFree.AddMinutes(10), e.ReadyAtUtc));
        Assert.Equal(4, _notices.Titles.Count(t => t == "Now cooking"));
    }

    [Fact]
    public async Task FinishedCountdownBecomesReadyAndTheNextOrderStarts()
    {
        MenuItem steak = await Dish("Kitchen ribeye", 10);
        var orders = new List<Order>();
        for (int n = 0; n < 5; n++) { orders.Add(await Order(ReservationStatus.Seated, 30 - n, steak)); }
        await _kitchen.AdvanceKitchenAsync();
        Assert.Equal(OrderStatus.Placed, await StatusOf(orders[4].Id));

        await FinishCooking(orders[0].Id);
        Assert.Equal(2, await _kitchen.AdvanceKitchenAsync());
        Assert.Equal(OrderStatus.ReadyToServe, await StatusOf(orders[0].Id));
        Assert.Equal(OrderStatus.Preparing, await StatusOf(orders[4].Id));
        Assert.Contains("Your food is ready", _notices.Titles);
        var ticket = await _db.KitchenTickets.AsNoTracking().SingleAsync(t => t.OrderId == orders[4].Id);
        Assert.Equal(10, ticket.CookingMinutes);
        Assert.Equal(ticket.StartedAtUtc.AddMinutes(10), ticket.ReadyAtUtc);
    }

    [Fact]
    public async Task OrderTakesItsLongestDishAndDrinksSkipTheQueue()
    {
        MenuItem soup = await Dish("Kitchen soup", 5);
        MenuItem lamb = await Dish("Kitchen lamb", 18);
        MenuItem tea = await Dish("Kitchen iced tea", null);
        MenuItem steak = await Dish("Kitchen sirloin", 12);
        for (int n = 0; n < 4; n++) { await Order(ReservationStatus.Seated, 40 - n, steak); }
        Order mixed = await Order(ReservationStatus.Seated, 20, soup, lamb, tea);
        Order drinks = await Order(ReservationStatus.Seated, 10, tea);
        await _kitchen.AdvanceKitchenAsync();

        // The kitchen is full, yet the drinks-only order is ready at once and the mixed order waits.
        Assert.Equal(OrderStatus.ReadyToServe, await StatusOf(drinks.Id));
        Assert.Equal(OrderStatus.Placed, await StatusOf(mixed.Id));
        KitchenBoard board = await _kitchen.GetKitchenBoardAsync();
        Assert.Equal(18, board.Find(mixed.Id)!.CookingMinutes);
        Assert.Equal(0, board.Find(drinks.Id)!.CookingMinutes);
        Assert.Equal(4, board.Cooking.Count);
    }

    [Fact]
    public async Task PreOrdersWaitForSeatingAndJoinTheLineWhenTheVisitStarts()
    {
        MenuItem steak = await Dish("Kitchen tomahawk", 15);
        Order notSeated = await Order(ReservationStatus.Confirmed, 600, steak);
        await _kitchen.AdvanceKitchenAsync();
        Assert.Equal(OrderStatus.Placed, await StatusOf(notSeated.Id));
        Assert.Null((await _kitchen.GetKitchenBoardAsync()).Find(notSeated.Id));

        // A pre-order placed ten hours ago counts from its visit start (an hour ago), so it queues behind
        // an order that a guest already at the table placed ninety minutes ago.
        for (int n = 0; n < 4; n++) { await Order(ReservationStatus.Seated, 100, 120, steak); }
        Order early = await Order(ReservationStatus.Seated, 90, 120, steak);
        await _db.Reservations.Where(r => r.Id == notSeated.ReservationId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.Seated));
        await _kitchen.AdvanceKitchenAsync();
        KitchenBoard board = await _kitchen.GetKitchenBoardAsync();
        Assert.Equal(new[] { early.Id, notSeated.Id }, board.Queue.Select(e => e.Order.Id));
    }

    [Fact]
    public async Task CancellingACookingOrderFreesItsSlot()
    {
        MenuItem steak = await Dish("Kitchen striploin", 20);
        var orders = new List<Order>();
        for (int n = 0; n < 5; n++) { orders.Add(await Order(ReservationStatus.Seated, 30 - n, steak)); }
        await _kitchen.AdvanceKitchenAsync();
        Assert.True((await _kitchen.ChangeStatusAsync(orders[1].Id, OrderStatus.Preparing, OrderStatus.Cancelled)).Succeeded);
        await _kitchen.AdvanceKitchenAsync();
        Assert.Equal(OrderStatus.Preparing, await StatusOf(orders[4].Id));
        Assert.Equal(4, (await _kitchen.GetKitchenBoardAsync()).Cooking.Count);
    }

    [Fact]
    public async Task ManualStartAlsoGetsACountdown()
    {
        MenuItem steak = await Dish("Kitchen wagyu", 25);
        Order order = await Order(ReservationStatus.Seated, 5, steak);
        Assert.True((await _kitchen.ChangeStatusAsync(order.Id, OrderStatus.Placed, OrderStatus.Preparing)).Succeeded);
        var ticket = await _db.KitchenTickets.AsNoTracking().SingleAsync(t => t.OrderId == order.Id);
        Assert.Equal(25, ticket.CookingMinutes);
        // Changing the dish later does not move a countdown that already started.
        await _db.MenuItems.Where(m => m.Id == steak.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.CookingMinutes, 5));
        Assert.Equal(25, (await _kitchen.GetKitchenBoardAsync()).Find(order.Id)!.CookingMinutes);
    }

    [Fact]
    public async Task OrdersAlreadyCookingBeforeTheUpgradeGetACountdown()
    {
        MenuItem pasta = await Dish("Kitchen legacy pasta", 8);
        Order order = await Order(ReservationStatus.Seated, 15, pasta);
        await _db.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Preparing));
        await _kitchen.AdvanceKitchenAsync();
        var entry = (await _kitchen.GetKitchenBoardAsync()).Find(order.Id)!;
        Assert.Equal(8, entry.CookingMinutes);
        Assert.NotNull(entry.ReadyAtUtc);
        await FinishCooking(order.Id);
        await _kitchen.AdvanceKitchenAsync();
        Assert.Equal(OrderStatus.ReadyToServe, await StatusOf(order.Id));
    }

    [Fact]
    public async Task MenuCookingTimeIsOptionalButLimited()
    {
        var menu = new MenuService(_db, new RestaurantService(_db), new FakeMenuImageStorage());
        var input = new MenuItemInput { Name = "Kitchen test lamb", Description = "d", MenuCategoryId = OrderTestData.GrillsId, Price = 30m, CookingMinutes = 15 };
        var created = await menu.CreateMenuItemAsync(input);
        Assert.True(created.Succeeded, string.Join(" ", created.Errors));
        Assert.Equal(15, (await _db.MenuItems.AsNoTracking().SingleAsync(m => m.Id == created.Value)).CookingMinutes);
        input.Name = "Kitchen test lemonade"; input.CookingMinutes = null;
        Assert.True((await menu.CreateMenuItemAsync(input)).Succeeded);
        input.Name = "Kitchen test slow roast"; input.CookingMinutes = MenuItem.MaxCookingMinutes + 1;
        Assert.False((await menu.CreateMenuItemAsync(input)).Succeeded);
        input.CookingMinutes = 0;
        Assert.False((await menu.CreateMenuItemAsync(input)).Succeeded);
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
