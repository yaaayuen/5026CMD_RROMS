using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Options;
using Rroms.Web.Services;
namespace Rroms.Tests.Alex;

/// <summary>Business acceptance tests against a relational database.</summary>
public class DiningExperienceServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new SqliteConnection("DataSource=:memory:");
    private readonly ApplicationDbContext _db;
    private readonly Notices _notices = new Notices();
    private readonly OrderProcessingService _orders;
    public DiningExperienceServiceTests()
    {
        _connection.Open();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.Users.Add(new ApplicationUser { Id = "alice", UserName = "alice@test.local", FullName = "Alice" });
        _db.Users.Add(new ApplicationUser { Id = "bob", UserName = "bob@test.local", FullName = "Bob" });
        _db.SaveChanges();
        _orders = new OrderProcessingService(_db, _notices);
    }
    public void Dispose() { _db.Dispose(); _connection.Dispose(); }
    private async Task<Order> AddOrder(ReservationStatus status, OrderStatus orderStatus = OrderStatus.Placed)
    {
        var reservation = new Reservation { CustomerId = "alice", DiningTableId = 1, PartySize = 2,
            Status = status, StartUtc = DateTime.UtcNow.AddDays(1), EndUtc = DateTime.UtcNow.AddDays(1).AddHours(2) };
        var order = new Order { Reservation = reservation, CustomerId = "alice", Status = orderStatus, PlacedAtUtc = DateTime.UtcNow };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return order;
    }

    [Theory]
    [InlineData(ReservationStatus.Pending, false)]
    [InlineData(ReservationStatus.Confirmed, false)]
    [InlineData(ReservationStatus.Seated, true)]
    [InlineData(ReservationStatus.Completed, false)]
    [InlineData(ReservationStatus.Cancelled, false)]
    [InlineData(ReservationStatus.NoShow, false)]
    public async Task KitchenAndPreparationRequireSeated(ReservationStatus reservation, bool allowed)
    {
        var order = await AddOrder(reservation);
        Assert.Equal(allowed, (await _orders.GetKitchenOrdersAsync()).Any());
        Assert.Equal(allowed, (await _orders.ChangeStatusAsync(order.Id, OrderStatus.Placed, OrderStatus.Preparing)).Succeeded);
        Assert.Equal(allowed ? 1 : 0, _notices.Users.Count);
    }

    [Fact]
    public async Task SeatingMakesSavedPreorderAppearWithoutRecreatingIt()
    {
        var order = await AddOrder(ReservationStatus.Confirmed);
        Assert.Empty(await _orders.GetKitchenOrdersAsync());
        await ReservationService().SeatAsync(order.ReservationId);
        Assert.Equal(order.Id, Assert.Single(await _orders.GetKitchenOrdersAsync()).Id);
    }

    [Fact]
    public async Task FullFlowAndStaleDoubleClick()
    {
        var order = await AddOrder(ReservationStatus.Seated);
        OrderStatus[] path = { OrderStatus.Placed, OrderStatus.Preparing, OrderStatus.ReadyToServe, OrderStatus.Served, OrderStatus.Completed };
        for (int n = 0; n < path.Length - 1; n++)
        {
            Assert.True((await _orders.ChangeStatusAsync(order.Id, path[n], path[n + 1])).Succeeded);
            Assert.False((await _orders.ChangeStatusAsync(order.Id, path[n], path[n + 1])).Succeeded);
        }
        Assert.Equal(4, _notices.Users.Count);
    }

    [Fact]
    public async Task EveryInvalidTransitionIsRejectedByService()
    {
        var valid = new HashSet<(OrderStatus, OrderStatus)>
        {
            (OrderStatus.Placed, OrderStatus.Preparing), (OrderStatus.Placed, OrderStatus.Cancelled),
            (OrderStatus.Preparing, OrderStatus.ReadyToServe), (OrderStatus.Preparing, OrderStatus.Cancelled),
            (OrderStatus.ReadyToServe, OrderStatus.Served), (OrderStatus.ReadyToServe, OrderStatus.Cancelled),
            (OrderStatus.Served, OrderStatus.Completed)
        };
        foreach (OrderStatus from in Enum.GetValues<OrderStatus>())
        foreach (OrderStatus to in Enum.GetValues<OrderStatus>())
        {
            var order = await AddOrder(ReservationStatus.Seated, from);
            Assert.Equal(valid.Contains((from, to)), (await _orders.ChangeStatusAsync(order.Id, from, to)).Succeeded);
        }
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("staff")]
    [InlineData("noshow")]
    public async Task ReservationCancellationCancelsPlacedOrders(string action)
    {
        var order = await AddOrder(ReservationStatus.Confirmed);
        var booking = await _db.Reservations.FindAsync(order.ReservationId);
        if (action == "noshow")
        {
            booking!.StartUtc = DateTime.UtcNow.AddHours(-1);
            await _db.SaveChangesAsync();
        }
        var service = ReservationService();
        var result = action == "customer" ? await service.CancelByCustomerAsync("alice", order.ReservationId)
            : action == "staff" ? await service.CancelByStaffAsync(order.ReservationId)
            : await service.MarkNoShowAsync(order.ReservationId);
        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        _db.ChangeTracker.Clear();
        Assert.Equal(OrderStatus.Cancelled, (await _db.Orders.FindAsync(order.Id))!.Status);
        Assert.Equal(0, await _orders.CancelUnstartedOrdersForReservationAsync(order.ReservationId));
    }

    [Fact]
    public async Task BulkCancellationNeverTouchesPreparingOrOtherBookings()
    {
        var cancelled = await AddOrder(ReservationStatus.Cancelled);
        var preparing = new Order { CustomerId = "alice", ReservationId = cancelled.ReservationId, Status = OrderStatus.Preparing };
        _db.Orders.Add(preparing); await _db.SaveChangesAsync();
        var other = await AddOrder(ReservationStatus.Confirmed);
        Assert.Equal(1, await _orders.CancelUnstartedOrdersForReservationAsync(cancelled.ReservationId));
        _db.ChangeTracker.Clear();
        Assert.Equal(OrderStatus.Preparing, (await _db.Orders.FindAsync(preparing.Id))!.Status);
        Assert.Equal(OrderStatus.Placed, (await _db.Orders.FindAsync(other.Id))!.Status);
    }

    [Fact]
    public async Task CustomerOrdersArePrivate()
    {
        await AddOrder(ReservationStatus.Seated);
        Assert.Single(await _orders.GetCustomerOrdersAsync("alice"));
        Assert.Empty(await _orders.GetCustomerOrdersAsync("bob"));
    }

    [Fact]
    public async Task FeedbackRequiresOwnershipCompletionAndSingleSubmission()
    {
        var order = await AddOrder(ReservationStatus.Confirmed);
        var service = new FeedbackService(_db);
        Assert.False((await service.SubmitAsync("alice", order.ReservationId, 5, "Nice")).Succeeded);
        var reservation = await _db.Reservations.FindAsync(order.ReservationId);
        reservation!.Status = ReservationStatus.Completed; await _db.SaveChangesAsync();
        Assert.False((await service.SubmitAsync("bob", order.ReservationId, 5, "Nice")).Succeeded);
        Assert.False((await service.SubmitAsync("alice", order.ReservationId, 0, "Nice")).Succeeded);
        Assert.False((await service.SubmitAsync("alice", order.ReservationId, 6, "Nice")).Succeeded);
        Assert.True((await service.SubmitAsync("alice", order.ReservationId, 4, "Good food")).Succeeded);
        Assert.False((await service.SubmitAsync("alice", order.ReservationId, 5, "Again")).Succeeded);
        Assert.Empty(await service.GetEligibleAsync("alice"));
        Assert.Single(await service.GetAsync(4, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)));
        Assert.Empty(await service.GetAsync(5, null, null));
        _db.Feedback.Add(new Feedback { ReservationId = order.ReservationId, Rating = 5 });
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task AnnouncementsRespectWindowAndNotifyOnlyOnPublication()
    {
        var service = new AnnouncementService(_db, _notices);
        var item = new Announcement { Title = "Lunch", Message = "Lunch campaign", IsPromotion = true,
            StartUtc = DateTime.UtcNow.AddHours(-1), EndUtc = DateTime.UtcNow.AddHours(1) };
        Assert.True((await service.SaveAsync(item)).Succeeded);
        Assert.Empty(await service.GetAsync(true));
        item = Assert.Single(await service.GetAsync(false)); item.IsPublished = true;
        Assert.True((await service.SaveAsync(item)).Succeeded);
        Assert.Single(await service.GetAsync(true)); Assert.Single(_notices.Roles);
        Assert.True((await service.SaveAsync(item)).Succeeded); Assert.Single(_notices.Roles);
        item.EndUtc = item.StartUtc;
        Assert.False((await service.SaveAsync(item)).Succeeded);
        item.EndUtc = DateTime.UtcNow.AddHours(1); item.IsPublished = false;
        Assert.True((await service.SaveAsync(item)).Succeeded);
        Assert.Empty(await service.GetAsync(true));
    }

    [Fact]
    public async Task StaleReservationCancellationCannotOverwriteSeating()
    {
        var order = await AddOrder(ReservationStatus.Confirmed);
        var stale = await _db.Reservations.FindAsync(order.ReservationId);
        using var second = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        var latest = await second.Reservations.FindAsync(order.ReservationId);
        latest!.Status = ReservationStatus.Seated;
        await second.SaveChangesAsync();
        stale!.Status = ReservationStatus.Cancelled;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => _orders.CancelUnstartedOrdersForReservationAsync(order.ReservationId));
        _db.ChangeTracker.Clear();
        Assert.Equal(ReservationStatus.Seated, (await _db.Reservations.FindAsync(order.ReservationId))!.Status);
        Assert.Equal(OrderStatus.Placed, (await _db.Orders.FindAsync(order.Id))!.Status);
    }

    private ReservationService ReservationService()
    {
        var rules = Microsoft.Extensions.Options.Options.Create(new ReservationRulesOptions());
        return new ReservationService(_db, new RestaurantService(_db), new ReservationTimeRules(rules), rules, _notices, _orders);
    }

    private class Notices : INotificationService
    {
        public List<string> Users { get; } = new List<string>();
        public List<string> Roles { get; } = new List<string>();
        public Task NotifyUserAsync(string userId, NotificationType type, string title, string message, string? linkUrl = null)
        { Users.Add(userId); return Task.CompletedTask; }
        public Task NotifyRoleAsync(string roleName, NotificationType type, string title, string message, string? linkUrl = null)
        { Roles.Add(roleName); return Task.CompletedTask; }
    }
}
