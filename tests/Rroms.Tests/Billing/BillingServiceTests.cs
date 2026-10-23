using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Rroms.Tests.Menu;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Services;

namespace Rroms.Tests.Billing;

/// <summary>DA-1 service and relational constraint tests, using YJ's shared order builder.</summary>
public class BillingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly ApplicationDbContext _context;
    private readonly BillingNotificationRecorder _notifications = new BillingNotificationRecorder();
    private readonly PaymentRaceInterceptor _race = new PaymentRaceInterceptor();
    private readonly BillingService _service;

    /// <summary>Creates an isolated relational database for each test.</summary>
    public BillingServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        var intercepted = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection)
            .AddInterceptors(_race).Options;
        _context = new ApplicationDbContext(intercepted);
        _context.Database.EnsureCreated();
        _service = new BillingService(_context, _notifications, NullLogger<BillingService>.Instance);
    }
    /// <summary>Releases the database after each test.</summary>
    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>Each supported method saves the order total and a confirmation after the payment is durable.</summary>
    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.CardTerminal)]
    [InlineData(PaymentMethod.EWallet)]
    [InlineData(PaymentMethod.SimulatedOnline)]
    public async Task RecordsServerTotalAndNotifiesAfterSave(PaymentMethod method)
    {
        var (order, staff) = await SeedAsync();
        // Price changes after ordering must not change either the bill or the payment.
        order.Items.Single().MenuItem!.Price = 999m;
        await _context.SaveChangesAsync();
        _notifications.BeforeNotify = async () =>
        {
            using var other = new ApplicationDbContext(_options);
            Assert.True(await other.Payments.AnyAsync(p => p.OrderId == order.Id));
        };
        DateTime before = DateTime.UtcNow;
        var result = await _service.RecordPaymentAsync(order.Id, method, staff.Id);
        Assert.True(result.Succeeded);
        Assert.True(result.Value!.NotificationSent);
        Payment payment = await _context.Payments.SingleAsync();
        Assert.Equal(40m, payment.Amount);
        Assert.Equal(method, payment.Method);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(staff.Id, payment.StaffId);
        Assert.InRange(payment.PaidAtUtc, before, DateTime.UtcNow);
        Assert.Equal(1, _notifications.Count);
        Assert.Equal(order.CustomerId, _notifications.UserId);
        Assert.Equal(NotificationType.PaymentConfirmed, _notifications.Type);
        Assert.Equal($"/Customer/Receipts/Details/{payment.Id}", _notifications.Link);
        if (method == PaymentMethod.SimulatedOnline)
        {
            Assert.Contains("no real payment processed", _notifications.Message);
        }
        var bill = await _service.GetBillAsync(order.Id);
        Assert.Equal(20m, bill!.Lines.Single().UnitPrice);
        Assert.Equal(order.TotalAmount, bill.TotalAmount);
    }

    /// <summary>Billing trusts YJ's saved total rather than recomputing from lines.</summary>
    [Fact]
    public async Task UsesOrderTotalRatherThanRecalculatingLines()
    {
        var (order, staff) = await SeedAsync();
        order.TotalAmount = 35m;
        await _context.SaveChangesAsync();
        var result = await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, staff.Id);
        Assert.True(result.Succeeded);
        Assert.Equal(35m, (await _context.Payments.SingleAsync()).Amount);
    }

    /// <summary>Only the terminal Completed status is billable.</summary>
    [Theory]
    [InlineData(OrderStatus.Placed)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.ReadyToServe)]
    [InlineData(OrderStatus.Served)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task RejectsIncompleteOrCancelledOrder(OrderStatus status)
    {
        var (order, staff) = await SeedAsync();
        order.Status = status;
        await _context.SaveChangesAsync();
        var result = await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, staff.Id);
        Assert.False(result.Succeeded);
        Assert.Contains(BillingService.NotCompletedMessage, result.Errors);
        Assert.Empty(await _context.Payments.ToListAsync());
        Assert.Equal(0, _notifications.Count);
    }

    /// <summary>Undefined payment methods cannot enter the database through the service.</summary>
    [Fact]
    public async Task RejectsUnknownMethod()
    {
        var (order, staff) = await SeedAsync();
        var result = await _service.RecordPaymentAsync(order.Id, (PaymentMethod)999, staff.Id);
        Assert.Contains(BillingService.InvalidMethodMessage, result.Errors);
        Assert.Empty(await _context.Payments.ToListAsync());
    }

    /// <summary>Missing orders return an explained failure.</summary>
    [Fact]
    public async Task RejectsMissingOrder()
    {
        var (_, staff) = await SeedAsync();
        var result = await _service.RecordPaymentAsync(int.MaxValue, PaymentMethod.Cash, staff.Id);
        Assert.Contains(BillingService.MissingOrderMessage, result.Errors);
        Assert.Null(await _service.GetBillAsync(int.MaxValue));
    }

    /// <summary>Both employee roles may record payments.</summary>
    [Theory]
    [InlineData(RoleNames.Staff)]
    [InlineData(RoleNames.Administrator)]
    public async Task AllowsStaffAndAdministrator(string role)
    {
        var (order, staff) = await SeedAsync(role);
        Assert.True((await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, staff.Id)).Succeeded);
    }

    /// <summary>Service validation also blocks customers, missing users and deactivated staff.</summary>
    [Fact]
    public async Task RejectsUnauthorisedRecorder()
    {
        var (order, staff) = await SeedAsync();
        foreach (string id in new[] { order.CustomerId, "missing-user", string.Empty })
        {
            var result = await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, id);
            Assert.Contains(BillingService.InvalidStaffMessage, result.Errors);
        }
        staff.IsActive = false;
        await _context.SaveChangesAsync();
        var inactive = await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, staff.Id);
        Assert.Contains(BillingService.InvalidStaffMessage, inactive.Errors);
        Assert.Empty(await _context.Payments.ToListAsync());
    }

    /// <summary>Replaying the form creates neither a second payment nor a second confirmation.</summary>
    [Fact]
    public async Task RepeatedPaymentIsRejected()
    {
        var (order, staff) = await SeedAsync();
        await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, staff.Id);
        var repeat = await _service.RecordPaymentAsync(order.Id, PaymentMethod.EWallet, staff.Id);
        Assert.Contains(BillingService.AlreadyPaidMessage, repeat.Errors);
        Assert.Single(await _context.Payments.ToListAsync());
        Assert.Equal(1, _notifications.Count);
    }

    /// <summary>A competing insert between check and save is rejected by the index and handled by the service.</summary>
    [Fact]
    public async Task SimultaneousWinnerIsHandledWithoutSecondPayment()
    {
        var (order, staff) = await SeedAsync();
        _race.BeforePaymentSave = async () =>
        {
            using var winner = new ApplicationDbContext(_options);
            winner.Payments.Add(NewPayment(order.Id, staff.Id));
            await winner.SaveChangesAsync();
        };
        var result = await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, staff.Id);
        Assert.Contains(BillingService.AlreadyPaidMessage, result.Errors);
        Assert.Single(await _context.Payments.ToListAsync());
        Assert.Equal(0, _notifications.Count);
        Assert.DoesNotContain(_context.ChangeTracker.Entries<Payment>(), e => e.State == EntityState.Added);
    }

    /// <summary>The database blocks duplicate Paid rows even when the service is bypassed.</summary>
    [Fact]
    public async Task UniqueIndexRejectsSecondPaidRecordAcrossContexts()
    {
        var (order, staff) = await SeedAsync();
        _context.Payments.Add(NewPayment(order.Id, staff.Id));
        await _context.SaveChangesAsync();
        using var second = new ApplicationDbContext(_options);
        second.Payments.Add(NewPayment(order.Id, staff.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    /// <summary>Historical non-Paid rows do not consume the unique Paid slot.</summary>
    [Fact]
    public async Task FilterAllowsNonPaidHistory()
    {
        var (order, staff) = await SeedAsync();
        foreach (PaymentStatus status in new[] { PaymentStatus.Unpaid, PaymentStatus.Voided })
        {
            Payment history = NewPayment(order.Id, staff.Id);
            history.Status = status;
            _context.Payments.Add(history);
        }
        await _context.SaveChangesAsync();
        Assert.True((await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, staff.Id)).Succeeded);
        Assert.Equal(3, await _context.Payments.CountAsync());
        Assert.Single(await _context.Payments.Where(p => p.Status == PaymentStatus.Paid).ToListAsync());
    }

    /// <summary>Negative amounts and invalid enum strings are rejected even for direct writes.</summary>
    [Theory]
    [InlineData("negative")]
    [InlineData("method")]
    [InlineData("status")]
    public async Task DatabaseRejectsInvalidPayment(string kind)
    {
        var (order, staff) = await SeedAsync();
        Payment payment = NewPayment(order.Id, staff.Id);
        if (kind == "negative") { payment.Amount = -1m; }
        if (kind == "method") { payment.Method = (PaymentMethod)999; }
        if (kind == "status") { payment.Status = (PaymentStatus)999; }
        _context.Payments.Add(payment);
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    /// <summary>A notification outage must not make a saved payment look unpaid.</summary>
    [Fact]
    public async Task NotificationFailureStillReturnsSavedReceipt()
    {
        var (order, staff) = await SeedAsync();
        _notifications.ThrowOnNotify = true;
        var result = await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, staff.Id);
        Assert.True(result.Succeeded);
        Assert.False(result.Value!.NotificationSent);
        Assert.NotNull(await _service.GetReceiptForStaffAsync(result.Value.PaymentId));
        Assert.Single(await _context.Payments.ToListAsync());
    }

    /// <summary>The bill list includes only Completed orders and hides Paid orders by default.</summary>
    [Fact]
    public async Task CompletedListFiltersPaidOrders()
    {
        var (order, staff) = await SeedAsync();
        MenuItem dish = await OrderTestData.AddDishAsync(_context, "Unfinished dish");
        await OrderTestData.AddOrderAsync(_context, dish.Id);
        Assert.Single(await _service.GetCompletedOrdersAsync());
        await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, staff.Id);
        Assert.Empty(await _service.GetCompletedOrdersAsync());
        Assert.Single(await _service.GetCompletedOrdersAsync(includePaid: true));
    }

    /// <summary>Customer receipt lookup filters ownership and never exposes voided receipts.</summary>
    [Fact]
    public async Task ReceiptIsPrivateAndUsesSavedPaymentAmount()
    {
        var (order, staff) = await SeedAsync();
        var result = await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, staff.Id);
        int id = result.Value!.PaymentId;
        var receipt = await _service.GetReceiptForCustomerAsync(id, order.CustomerId);
        Assert.NotNull(receipt);
        Assert.Equal(40m, receipt.Amount);
        Assert.Null(await _service.GetReceiptForCustomerAsync(id, staff.Id));
        Assert.Null(await _service.GetReceiptForCustomerAsync(id, string.Empty));
        Assert.Null(await _service.GetReceiptForStaffAsync(int.MaxValue));
        Payment payment = await _context.Payments.SingleAsync();
        payment.Status = PaymentStatus.Voided;
        await _context.SaveChangesAsync();
        Assert.Null(await _service.GetReceiptForCustomerAsync(id, order.CustomerId));
    }

    // ============================== Whole-visit bill (pay once, one receipt) ==============================//

    /// <summary>A pre-order and a second order of the same visit are paid in one step and shown on one receipt.</summary>
    [Fact]
    public async Task VisitPayment_PaysEveryUnpaidOrderOnceWithOneReceipt()
    {
        var (order, staff) = await SeedAsync();
        Order second = await AddSecondOrderAsync(order, OrderStatus.Completed, 30m);

        var result = await _service.RecordVisitPaymentAsync(order.ReservationId, PaymentMethod.Cash, staff.Id);

        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        Assert.Equal(2, await _context.Payments.CountAsync(p => p.Status == PaymentStatus.Paid));
        Assert.Equal(1, _notifications.Count);
        var receipt = await _service.GetReceiptForCustomerAsync(result.Value!.PaymentId, order.CustomerId);
        Assert.NotNull(receipt);
        Assert.Equal(new[] { order.Id, second.Id }, receipt!.Bills.Select(b => b.OrderId));
        Assert.Equal(70m, receipt.TotalPaid);
        var visit = await _service.GetVisitBillAsync(order.ReservationId);
        Assert.Empty(visit!.UnpaidOrders);
    }

    /// <summary>The visit cannot be paid while one of its orders is still with the kitchen; nothing is saved.</summary>
    [Fact]
    public async Task VisitPayment_OrderStillInKitchen_IsRejected()
    {
        var (order, staff) = await SeedAsync();
        await AddSecondOrderAsync(order, OrderStatus.Served, 30m);

        var result = await _service.RecordVisitPaymentAsync(order.ReservationId, PaymentMethod.Cash, staff.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(BillingService.VisitNotReadyMessage, result.Errors);
        Assert.Equal(0, await _context.Payments.CountAsync());
    }

    /// <summary>Cancelled orders are not billed, and paying twice is refused.</summary>
    [Fact]
    public async Task VisitPayment_IgnoresCancelledOrdersAndCannotBeRepeated()
    {
        var (order, staff) = await SeedAsync();
        await AddSecondOrderAsync(order, OrderStatus.Cancelled, 30m);

        var first = await _service.RecordVisitPaymentAsync(order.ReservationId, PaymentMethod.EWallet, staff.Id);
        var again = await _service.RecordVisitPaymentAsync(order.ReservationId, PaymentMethod.EWallet, staff.Id);

        Assert.True(first.Succeeded);
        Assert.Equal(40m, (await _context.Payments.SingleAsync()).Amount);
        Assert.False(again.Succeeded);
        Assert.Contains(BillingService.NothingToPayMessage, again.Errors);
    }

    /// <summary>Adds another order (one line of the same dish) to the first order's visit.</summary>
    private async Task<Order> AddSecondOrderAsync(Order first, OrderStatus status, decimal total)
    {
        var second = new Order
        {
            ReservationId = first.ReservationId,
            CustomerId = first.CustomerId,
            Status = status,
            TotalAmount = total,
            PlacedAtUtc = first.PlacedAtUtc.AddMinutes(30),
            UpdatedAtUtc = first.PlacedAtUtc.AddMinutes(30)
        };
        second.Items.Add(new OrderItem { MenuItemId = first.Items.Single().MenuItemId, Quantity = 1, UnitPrice = total });
        _context.Orders.Add(second);
        await _context.SaveChangesAsync();
        return second;
    }

    private async Task<(Order Order, ApplicationUser Staff)> SeedAsync(string roleName = RoleNames.Staff)
    {
        MenuItem dish = await OrderTestData.AddDishAsync(_context, "Billing test dish");
        Order order = await OrderTestData.AddOrderAsync(_context, dish.Id);
        order.Status = OrderStatus.Completed;
        ApplicationUser staff = await OrderTestData.AddCustomerAsync(_context);
        staff.FullName = "Billing Staff";
        var role = new IdentityRole(roleName) { NormalizedName = roleName.ToUpperInvariant() };
        _context.Roles.Add(role);
        _context.UserRoles.Add(new IdentityUserRole<string> { UserId = staff.Id, RoleId = role.Id });
        await _context.SaveChangesAsync();
        return (order, staff);
    }

    private static Payment NewPayment(int orderId, string staffId)
    {
        return new Payment
        {
            OrderId = orderId, StaffId = staffId, Amount = 40m,
            Method = PaymentMethod.Cash, Status = PaymentStatus.Paid, PaidAtUtc = DateTime.UtcNow
        };
    }
}
