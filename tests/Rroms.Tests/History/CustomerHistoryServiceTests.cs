using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Rroms.Tests.Menu;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Tests.History;

/// <summary>
/// YJ-5 acceptance tests for <see cref="CustomerHistoryService"/>: a customer sees only their own reservations,
/// orders and payments, newest first, limited in number. Owner: YJ.
/// </summary>
/// <remarks>
/// Written BEFORE the service (test-first): against the empty shell they all fail (red) and turn green as the
/// rules are implemented. The privacy tests matter most: another customer's records must never appear.
/// </remarks>
public class CustomerHistoryServiceTests : IDisposable
{
    private static readonly DateTime Day1 = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day3 = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly CustomerHistoryService _service;

    /// <summary>Runs before EVERY test: a brand-new in-memory database, so tests never affect each other.</summary>
    public CustomerHistoryServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _service = new CustomerHistoryService(_context);
    }

    /// <summary>Runs after EVERY test: closing the connection deletes the database.</summary>
    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ============================== Privacy: only my own records ==============================//

    /// <summary>A customer sees their own reservations and never another customer's.</summary>
    [Fact]
    public async Task Reservations_ShowOnlyTheCustomersOwn()
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser ben = await OrderTestData.AddCustomerAsync(_context);
        Reservation annasFirst = await HistoryTestData.AddReservationAsync(_context, anna.Id, Day1);
        Reservation annasSecond = await HistoryTestData.AddReservationAsync(_context, anna.Id, Day2);
        await HistoryTestData.AddReservationAsync(_context, ben.Id, Day3);

        CustomerHistory history = await _service.GetHistoryAsync(anna.Id);

        Assert.Equal(new[] { annasSecond.Id, annasFirst.Id }, history.Reservations.Select(row => row.Id).ToArray());
    }

    /// <summary>A customer sees their own orders and never another customer's.</summary>
    [Fact]
    public async Task Orders_ShowOnlyTheCustomersOwn()
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser ben = await OrderTestData.AddCustomerAsync(_context);
        Order annasOrder = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day1);
        await HistoryTestData.AddOrderAsync(_context, ben.Id, Day2);

        CustomerHistory history = await _service.GetHistoryAsync(anna.Id);

        Assert.Equal(new[] { annasOrder.Id }, history.Orders.Select(row => row.Id).ToArray());
    }

    /// <summary>A customer sees payments for their own orders and never for another customer's.</summary>
    [Fact]
    public async Task Payments_ShowOnlyThoseForTheCustomersOwnOrders()
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser ben = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser staff = await HistoryTestData.AddStaffAsync(_context);
        Order annasOrder = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day1);
        Order bensOrder = await HistoryTestData.AddOrderAsync(_context, ben.Id, Day1);
        Payment annasPayment = await HistoryTestData.AddPaymentAsync(_context, annasOrder, staff.Id, Day2);
        await HistoryTestData.AddPaymentAsync(_context, bensOrder, staff.Id, Day2);

        CustomerHistory history = await _service.GetHistoryAsync(anna.Id);

        Assert.Equal(new[] { annasPayment.Id }, history.Payments.Select(row => row.ReceiptNumber).ToArray());
    }

    // ============================== Order of the rows ==============================//

    /// <summary>Reservations are listed with the latest visit first.</summary>
    [Fact]
    public async Task Reservations_AreNewestFirst()
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);
        Reservation middle = await HistoryTestData.AddReservationAsync(_context, anna.Id, Day2);
        Reservation oldest = await HistoryTestData.AddReservationAsync(_context, anna.Id, Day1);
        Reservation newest = await HistoryTestData.AddReservationAsync(_context, anna.Id, Day3);

        CustomerHistory history = await _service.GetHistoryAsync(anna.Id);

        Assert.Equal(new[] { newest.Id, middle.Id, oldest.Id }, history.Reservations.Select(row => row.Id).ToArray());
    }

    /// <summary>Orders are listed with the latest first.</summary>
    [Fact]
    public async Task Orders_AreNewestFirst()
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);
        Order middle = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day2);
        Order oldest = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day1);
        Order newest = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day3);

        CustomerHistory history = await _service.GetHistoryAsync(anna.Id);

        Assert.Equal(new[] { newest.Id, middle.Id, oldest.Id }, history.Orders.Select(row => row.Id).ToArray());
    }

    /// <summary>Payments are listed with the latest first.</summary>
    [Fact]
    public async Task Payments_AreNewestFirst()
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser staff = await HistoryTestData.AddStaffAsync(_context);
        Order first = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day1);
        Order second = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day1);
        Order third = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day1);
        Payment middle = await HistoryTestData.AddPaymentAsync(_context, first, staff.Id, Day2);
        Payment oldest = await HistoryTestData.AddPaymentAsync(_context, second, staff.Id, Day1);
        Payment newest = await HistoryTestData.AddPaymentAsync(_context, third, staff.Id, Day3);

        CustomerHistory history = await _service.GetHistoryAsync(anna.Id);

        Assert.Equal(new[] { newest.Id, middle.Id, oldest.Id }, history.Payments.Select(row => row.ReceiptNumber).ToArray());
    }

    // ============================== The limit ==============================//

    /// <summary>Each section is cut to the requested number of rows, keeping the newest.</summary>
    [Fact]
    public async Task Take_LimitsEverySectionAndKeepsTheNewest()
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser staff = await HistoryTestData.AddStaffAsync(_context);
        await HistoryTestData.AddReservationAsync(_context, anna.Id, Day1);
        Reservation newestReservation = await HistoryTestData.AddReservationAsync(_context, anna.Id, Day3);
        Reservation nextReservation = await HistoryTestData.AddReservationAsync(_context, anna.Id, Day2);
        Order oldestOrder = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day1);
        Order newestOrder = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day3);
        Order nextOrder = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day2);
        await HistoryTestData.AddPaymentAsync(_context, oldestOrder, staff.Id, Day1);
        Payment newestPayment = await HistoryTestData.AddPaymentAsync(_context, newestOrder, staff.Id, Day3);
        Payment nextPayment = await HistoryTestData.AddPaymentAsync(_context, nextOrder, staff.Id, Day2);

        CustomerHistory history = await _service.GetHistoryAsync(anna.Id, take: 2);

        Assert.Equal(new[] { newestReservation.Id, nextReservation.Id }, history.Reservations.Select(row => row.Id).ToArray());
        Assert.Equal(new[] { newestOrder.Id, nextOrder.Id }, history.Orders.Select(row => row.Id).ToArray());
        Assert.Equal(new[] { newestPayment.Id, nextPayment.Id }, history.Payments.Select(row => row.ReceiptNumber).ToArray());
    }

    /// <summary>The default limit is 20 rows per section.</summary>
    [Fact]
    public async Task DefaultTake_IsTwentyRowsPerSection()
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);

        for (int index = 0; index < 25; index++)
        {
            await HistoryTestData.AddOrderAsync(_context, anna.Id, Day1.AddMinutes(index));
        }

        CustomerHistory history = await _service.GetHistoryAsync(anna.Id);

        Assert.Equal(ICustomerHistoryService.DefaultTake, history.Orders.Count);
        Assert.Equal(20, history.Orders.Count);
    }

    /// <summary>Asking for zero or fewer rows gives three empty sections, not an error.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Take_ZeroOrLess_GivesEmptySections(int take)
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);
        await HistoryTestData.AddOrderAsync(_context, anna.Id, Day1);

        CustomerHistory history = await _service.GetHistoryAsync(anna.Id, take);

        Assert.Empty(history.Reservations);
        Assert.Empty(history.Orders);
        Assert.Empty(history.Payments);
    }

    // ============================== What is included ==============================//

    /// <summary>A cancelled order stays in the history with its status.</summary>
    [Fact]
    public async Task CancelledOrder_IsListedWithItsStatus()
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);
        Order cancelled = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day1, OrderStatus.Cancelled);

        CustomerHistory history = await _service.GetHistoryAsync(anna.Id);

        HistoryOrderRow row = Assert.Single(history.Orders);
        Assert.Equal(cancelled.Id, row.Id);
        Assert.Equal(OrderStatus.Cancelled, row.Status);
    }

    /// <summary>A cancelled reservation stays in the history with its status.</summary>
    [Fact]
    public async Task CancelledReservation_IsListedWithItsStatus()
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);
        Reservation cancelled = await HistoryTestData.AddReservationAsync(_context, anna.Id, Day1, ReservationStatus.Cancelled);

        CustomerHistory history = await _service.GetHistoryAsync(anna.Id);

        HistoryReservationRow row = Assert.Single(history.Reservations);
        Assert.Equal(cancelled.Id, row.Id);
        Assert.Equal(ReservationStatus.Cancelled, row.Status);
    }

    /// <summary>Only payments with status Paid are shown: unpaid and voided ones are not payments the customer made.</summary>
    [Fact]
    public async Task Payments_ShowOnlyPaidOnes()
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser staff = await HistoryTestData.AddStaffAsync(_context);
        Order paidOrder = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day1);
        Order unpaidOrder = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day1);
        Order voidedOrder = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day1);
        Payment paid = await HistoryTestData.AddPaymentAsync(_context, paidOrder, staff.Id, Day2, PaymentStatus.Paid);
        await HistoryTestData.AddPaymentAsync(_context, unpaidOrder, staff.Id, Day2, PaymentStatus.Unpaid);
        await HistoryTestData.AddPaymentAsync(_context, voidedOrder, staff.Id, Day2, PaymentStatus.Voided);

        CustomerHistory history = await _service.GetHistoryAsync(anna.Id);

        Assert.Equal(new[] { paid.Id }, history.Payments.Select(row => row.ReceiptNumber).ToArray());
    }

    // ============================== The values in a row ==============================//

    /// <summary>A reservation row carries the table, area, party size, time and status.</summary>
    [Fact]
    public async Task ReservationRow_CarriesTheBookingDetails()
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);
        Reservation reservation = await HistoryTestData.AddReservationAsync(_context, anna.Id, Day2, ReservationStatus.Seated);

        CustomerHistory history = await _service.GetHistoryAsync(anna.Id);

        HistoryReservationRow row = Assert.Single(history.Reservations);
        Assert.Equal(reservation.Id, row.Id);
        Assert.Equal(Day2, row.StartUtc);
        Assert.Equal(OrderTestData.SeededTableLabel, row.TableLabel);
        Assert.False(string.IsNullOrWhiteSpace(row.AreaName));
        Assert.Equal(reservation.PartySize, row.PartySize);
        Assert.Equal(ReservationStatus.Seated, row.Status);
    }

    /// <summary>An order row carries the number, time, saved total and status.</summary>
    [Fact]
    public async Task OrderRow_CarriesTheSavedTotal()
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);
        Order order = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day2, OrderStatus.Served, 43.00m);

        CustomerHistory history = await _service.GetHistoryAsync(anna.Id);

        HistoryOrderRow row = Assert.Single(history.Orders);
        Assert.Equal(order.Id, row.Id);
        Assert.Equal(Day2, row.PlacedAtUtc);
        Assert.Equal(43.00m, row.TotalAmount);
        Assert.Equal(OrderStatus.Served, row.Status);
    }

    /// <summary>A payment row carries the receipt number, time, amount, method and the order it paid.</summary>
    [Fact]
    public async Task PaymentRow_CarriesTheReceiptDetails()
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser staff = await HistoryTestData.AddStaffAsync(_context);
        Order order = await HistoryTestData.AddOrderAsync(_context, anna.Id, Day1, OrderStatus.Completed, 43.00m);
        Payment payment = await HistoryTestData.AddPaymentAsync(_context, order, staff.Id, Day2, PaymentStatus.Paid, PaymentMethod.EWallet);

        CustomerHistory history = await _service.GetHistoryAsync(anna.Id);

        HistoryPaymentRow row = Assert.Single(history.Payments);
        Assert.Equal(payment.Id, row.ReceiptNumber);
        Assert.Equal(Day2, row.PaidAtUtc);
        Assert.Equal(43.00m, row.Amount);
        Assert.Equal(PaymentMethod.EWallet, row.Method);
        Assert.Equal(order.Id, row.OrderId);
    }

    // ============================== Empty history ==============================//

    /// <summary>A customer with no records, and an unknown customer, both get three empty sections.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoRecords_GivesThreeEmptySections(bool unknownCustomer)
    {
        ApplicationUser anna = await OrderTestData.AddCustomerAsync(_context);
        string customerId = unknownCustomer ? "no-such-customer" : anna.Id;

        CustomerHistory history = await _service.GetHistoryAsync(customerId);

        Assert.Empty(history.Reservations);
        Assert.Empty(history.Orders);
        Assert.Empty(history.Payments);
    }
}
