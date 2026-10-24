using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.Infrastructure;
using Rroms.Tests.Menu;
using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Tests.AdminMonitoring;
/// <summary>DA-2 relational query and total reconciliation tests against isolated SQLite databases.</summary>
public class AdminMonitoringServiceTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new TestWebApplicationFactory();
    private readonly IServiceScope _scope;
    private readonly ApplicationDbContext _db;
    private readonly IMonitoringService _service;
    private static readonly DateOnly Day = new DateOnly(2026, 10, 22);
    private static readonly DateTime Start = MalaysiaTime.ToUtc(Day.ToDateTime(TimeOnly.MinValue));
    /// <summary>Resolves the real service, retaining all existing DA-7 registrations.</summary>
    public AdminMonitoringServiceTests()
    {
        _factory.CreateSecureClient().Dispose();
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _service = _scope.ServiceProvider.GetRequiredService<IMonitoringService>();
    }
    /// <summary>Releases the scoped database and application.</summary>
    public void Dispose() { _scope.Dispose(); _factory.Dispose(); }

    /// <summary>All lists include the first Malaysia midnight and final fraction, but not either adjacent day.</summary>
    [Fact]
    public async Task AllQueries_UseInclusiveMalaysiaDatesAndStableDescendingOrder()
    {
        await AddAsync(Start.AddTicks(-1), 1m);
        Order first = await AddAsync(Start, 2m);
        Order last = await AddAsync(Start.AddDays(1).AddTicks(-1), 3m);
        await AddAsync(Start.AddDays(1), 4m);
        var reservations = (await _service.GetReservationMonitoringAsync(new ReservationMonitoringFilter { From = Day, To = Day })).Value!;
        var orders = (await _service.GetOrderMonitoringAsync(new OrderMonitoringFilter { From = Day, To = Day })).Value!;
        var payments = (await _service.GetTransactionMonitoringAsync(new TransactionMonitoringFilter { From = Day, To = Day })).Value!;
        Assert.Equal(new[] { last.ReservationId, first.ReservationId }, reservations.Rows.Select(r => r.Id));
        Assert.Equal(new[] { last.Id, first.Id }, orders.Rows.Select(o => o.Id));
        Assert.Equal(new[] { last.Id, first.Id }, payments.Rows.Select(o => o.OrderId));
        Assert.Equal(5m, orders.OrderTotal);
        Assert.Equal(5m, payments.PaidTotal);
        Assert.Equal(4, reservations.GuestTotal);
    }

    /// <summary>Multi-day ranges include both selected calendar dates.</summary>
    [Fact]
    public async Task MultiDayRange_IncludesBothDates()
    {
        await AddAsync(Start, 1m);
        await AddAsync(Start.AddDays(1).AddHours(10), 2m);
        await AddAsync(Start.AddDays(2), 3m);
        var result = (await _service.GetOrderMonitoringAsync(new OrderMonitoringFilter { From = Day, To = Day.AddDays(1) })).Value!;
        Assert.Equal(2, result.Count);
        Assert.Equal(3m, result.OrderTotal);
    }

    /// <summary>The three date filters follow their own event timestamp rather than the booking date.</summary>
    [Fact]
    public async Task Filters_UseTheirOwnEventDates()
    {
        Order order = await AddAsync(Start.AddHours(3), 15m);
        order.Reservation!.StartUtc = Start.AddDays(-1);
        order.Reservation.EndUtc = Start.AddDays(-1).AddHours(2);
        var payment = _db.Payments.Single(p => p.OrderId == order.Id);
        payment.PaidAtUtc = Start.AddDays(1);
        await _db.SaveChangesAsync();
        Assert.Equal(0, (await _service.GetReservationMonitoringAsync(new ReservationMonitoringFilter { From = Day, To = Day })).Value!.Count);
        Assert.Equal(1, (await _service.GetOrderMonitoringAsync(new OrderMonitoringFilter { From = Day, To = Day })).Value!.Count);
        Assert.Equal(0, (await _service.GetTransactionMonitoringAsync(new TransactionMonitoringFilter { From = Day, To = Day })).Value!.Count);
    }

    /// <summary>Status and area combine with the range and inactive areas still retain historical records.</summary>
    [Fact]
    public async Task ReservationFilters_ReconcileCountAndGuests()
    {
        Order a = await AddAsync(Start, 1m);
        Order b = await AddAsync(Start.AddHours(1), 1m);
        Order c = await AddAsync(Start.AddHours(2), 1m);
        a.Reservation!.Status = ReservationStatus.Cancelled;
        a.Reservation.PartySize = 5;
        b.Reservation!.Status = ReservationStatus.Cancelled;
        b.Reservation.DiningTableId = 7;
        c.Reservation!.Status = ReservationStatus.Confirmed;
        (await _db.DiningAreas.FindAsync(1))!.IsActive = false;
        await _db.SaveChangesAsync();
        var result = (await _service.GetReservationMonitoringAsync(new ReservationMonitoringFilter
        { From = Day, To = Day, Status = ReservationStatus.Cancelled, AreaId = 1 })).Value!;
        Assert.Equal(a.ReservationId, Assert.Single(result.Rows).Id);
        Assert.Equal(5, result.GuestTotal);
        Assert.Equal(1, result.Count);
    }

    /// <summary>Saved totals, including fractional amounts, never change with menu prices or item counts.</summary>
    [Fact]
    public async Task OrderTotals_UseSavedAmountsAndSelectedStatus()
    {
        Order a = await AddAsync(Start, 0.01m);
        Order b = await AddAsync(Start.AddHours(1), 0.02m);
        Order cancelled = await AddAsync(Start.AddHours(2), 100m);
        a.Status = OrderStatus.Completed; b.Status = OrderStatus.Completed;
        cancelled.Status = OrderStatus.Cancelled;
        a.Items.First().MenuItem!.Price = 999m;
        await _db.SaveChangesAsync();
        var completed = (await _service.GetOrderMonitoringAsync(new OrderMonitoringFilter { From = Day, To = Day, Status = OrderStatus.Completed })).Value!;
        Assert.Equal(2, completed.Count);
        Assert.Equal(0.03m, completed.OrderTotal);
        var all = (await _service.GetOrderMonitoringAsync(new OrderMonitoringFilter { From = Day, To = Day })).Value!;
        Assert.Equal(100.03m, all.OrderTotal);
    }

    /// <summary>Paid totals split demo from non-demo and exclude Unpaid/Voided rows even when visible.</summary>
    [Fact]
    public async Task Payments_ReconcileAllTotalsAndCombinedFilters()
    {
        await AddAsync(Start, 20.25m, PaymentMethod.Cash);
        await AddAsync(Start.AddHours(1), 30.50m, PaymentMethod.CardTerminal);
        await AddAsync(Start.AddHours(2), 10m, PaymentMethod.SimulatedOnline);
        await AddAsync(Start.AddHours(3), 888m, PaymentMethod.Cash, PaymentStatus.Voided);
        await AddAsync(Start.AddHours(4), 777m, PaymentMethod.Cash, PaymentStatus.Unpaid);
        var all = (await _service.GetTransactionMonitoringAsync(new TransactionMonitoringFilter { From = Day, To = Day })).Value!;
        Assert.Equal(4, all.Count);
        Assert.Equal(50.75m, all.PaidTotal);
        Assert.Equal(0m, all.DemoPaidTotal);
        Assert.Equal(50.75m, all.NonDemoPaidTotal);
        var card = (await _service.GetTransactionMonitoringAsync(new TransactionMonitoringFilter
        { From = Day, To = Day, Status = PaymentStatus.Paid, Method = PaymentMethod.CardTerminal })).Value!;
        Assert.Single(card.Rows);
        Assert.Equal(30.50m, card.PaidTotal);
        Assert.Equal(0m, card.DemoPaidTotal);
        var voided = (await _service.GetTransactionMonitoringAsync(new TransactionMonitoringFilter
        { From = Day, To = Day, Status = PaymentStatus.Voided })).Value!;
        Assert.Single(voided.Rows);
        Assert.Equal(0m, voided.PaidTotal);
    }

    /// <summary>Empty filters return empty rows and zero totals.</summary>
    [Fact]
    public async Task EmptyDatabase_HasZeroTotals()
    {
        var r = (await _service.GetReservationMonitoringAsync(new ReservationMonitoringFilter { From = Day, To = Day })).Value!;
        var o = (await _service.GetOrderMonitoringAsync(new OrderMonitoringFilter { From = Day, To = Day })).Value!;
        var p = (await _service.GetTransactionMonitoringAsync(new TransactionMonitoringFilter { From = Day, To = Day })).Value!;
        Assert.Empty(r.Rows); Assert.Equal(0, r.GuestTotal);
        Assert.Empty(o.Rows); Assert.Equal(0m, o.OrderTotal);
        Assert.Empty(p.Rows); Assert.Equal(0m, p.PaidTotal);
    }

    /// <summary>Invalid ranges fail for every monitoring list without date conversion exceptions.</summary>
    [Theory]
    [InlineData("reversed")]
    [InlineData("minimum")]
    [InlineData("maximum")]
    public async Task InvalidRanges_AreRejected(string kind)
    {
        DateOnly from = Day; DateOnly to = Day;
        if (kind == "reversed") { from = Day.AddDays(1); }
        if (kind == "minimum") { from = DateOnly.MinValue; }
        if (kind == "maximum") { to = DateOnly.MaxValue; }
        Assert.False((await _service.GetReservationMonitoringAsync(new ReservationMonitoringFilter { From = from, To = to })).Succeeded);
        Assert.False((await _service.GetOrderMonitoringAsync(new OrderMonitoringFilter { From = from, To = to })).Succeeded);
        Assert.False((await _service.GetTransactionMonitoringAsync(new TransactionMonitoringFilter { From = from, To = to })).Succeeded);
    }

    /// <summary>Undefined enum values and nonexistent areas fail closed.</summary>
    [Fact]
    public async Task InvalidEnumsAndArea_AreRejected()
    {
        Assert.False((await _service.GetReservationMonitoringAsync(new ReservationMonitoringFilter { Status = (ReservationStatus)999 })).Succeeded);
        Assert.False((await _service.GetReservationMonitoringAsync(new ReservationMonitoringFilter { AreaId = -1 })).Succeeded);
        Assert.False((await _service.GetOrderMonitoringAsync(new OrderMonitoringFilter { Status = (OrderStatus)999 })).Succeeded);
        Assert.False((await _service.GetTransactionMonitoringAsync(new TransactionMonitoringFilter { Status = (PaymentStatus)999 })).Succeeded);
        Assert.False((await _service.GetTransactionMonitoringAsync(new TransactionMonitoringFilter { Method = (PaymentMethod)999 })).Succeeded);
    }

    private async Task<Order> AddAsync(DateTime atUtc, decimal amount, PaymentMethod method = PaymentMethod.Cash, PaymentStatus status = PaymentStatus.Paid)
    {
        var dish = await OrderTestData.AddDishAsync(_db, "Monitoring dish");
        Order order = await OrderTestData.AddOrderAsync(_db, dish.Id);
        order.TotalAmount = amount; order.PlacedAtUtc = atUtc;
        Reservation reservation = (await _db.Reservations.FindAsync(order.ReservationId))!;
        reservation.StartUtc = atUtc; reservation.EndUtc = atUtc.AddHours(2);
        var payment = new Payment { OrderId = order.Id, StaffId = order.CustomerId,
            Amount = amount, Method = method, Status = status, PaidAtUtc = atUtc };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return order;
    }
}
