using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
using Rroms.Web.Services;

namespace Rroms.Tests.Reservations;

/// <summary>
/// WL-3 acceptance: status changes (confirm, seat, complete, cancel, no-show), booking changes that
/// re-check availability, ownership rules for customers, and the order check YJ uses.
/// </summary>
/// <remarks>
/// Same set-up as ReservationServiceTests: a fresh in-memory database per test with the WL-1 seed data
/// (open 11:00-22:00 every day; T01 and T02 have 2 seats, T06 has 8).
/// Bookings that must already have started (no-show, late cancel, complete) are inserted directly,
/// because the service rightly refuses to create bookings in the past.
/// </remarks>
public class ReservationStatusChangeTests : IDisposable
{
    private const int TableT01Id = 1;
    private const int TableT02Id = 2;

    private const string CustomerA = "customer-a";
    private const string CustomerB = "customer-b";
    private const string CustomerAName = "Alice Tan";
    private const string CustomerBName = "Ben Lim";

    private static readonly TimeOnly SevenPm = new TimeOnly(19, 0);
    private static readonly TimeOnly HalfPastSeven = new TimeOnly(19, 30);

    /// <summary>A visit date one week ahead (Malaysia), always in the future and inside the booking window.</summary>
    private static readonly DateOnly VisitDate = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow)).AddDays(7);

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly RecordingNotificationService _notifications;
    private readonly ReservationTimeRules _timeRules;
    private readonly ReservationService _service;

    /// <summary>Runs before EVERY test: a new database with the seed data and two customers.</summary>
    public ReservationStatusChangeTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _context.Users.Add(CreateCustomer(CustomerA, CustomerAName));
        _context.Users.Add(CreateCustomer(CustomerB, CustomerBName));
        _context.SaveChanges();

        Microsoft.Extensions.Options.IOptions<ReservationRulesOptions> rules =
            Microsoft.Extensions.Options.Options.Create(new ReservationRulesOptions());
        _timeRules = new ReservationTimeRules(rules);
        _notifications = new RecordingNotificationService();
        _service = new ReservationService(
            _context, new RestaurantService(_context), _timeRules, rules, _notifications,
            new OrderProcessingService(_context, _notifications));
    }

    /// <summary>Runs after EVERY test: closing the connection deletes the database.</summary>
    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ============================== 1. Staff status changes ==============================//

    [Fact]
    public async Task Confirm_PendingBooking_BecomesConfirmedAndNotifiesCustomer()
    {
        int id = await BookAsync(CustomerA, SevenPm, TableT01Id);

        ServiceResult result = await _service.ConfirmAsync(id);

        Assert.True(result.Succeeded);
        Assert.Equal(ReservationStatus.Confirmed, await StatusOfAsync(id));
        Assert.Contains(CustomerA, _notifications.NotifiedUsers);
    }

    [Fact]
    public async Task Seat_PendingBooking_IsRejectedBecauseItMustBeConfirmedFirst()
    {
        int id = await BookAsync(CustomerA, SevenPm, TableT01Id);

        ServiceResult result = await _service.SeatAsync(id);

        Assert.False(result.Succeeded);
        Assert.StartsWith("A Pending booking cannot be changed to Seated", result.Errors[0]);
        Assert.Equal(ReservationStatus.Pending, await StatusOfAsync(id));
    }

    [Fact]
    public async Task FullFlow_ConfirmSeatComplete_EndsCompleted()
    {
        // Starts in 10 minutes, so the guests may already be seated (early seating rule).
        DateTime startUtc = WholeSecond(DateTime.UtcNow.AddMinutes(10));
        int id = await InsertReservationAsync(CustomerA, TableT01Id, startUtc, ReservationStatus.Pending);

        Assert.True((await _service.ConfirmAsync(id)).Succeeded);
        Assert.True((await _service.SeatAsync(id)).Succeeded);
        Assert.True((await _service.CompleteAsync(id)).Succeeded);

        Assert.Equal(ReservationStatus.Completed, await StatusOfAsync(id));
    }

    [Fact]
    public async Task Complete_GuestsLeaveEarly_OnlyUnusedSlotsAreReleased()
    {
        // Seated 45 minutes ago: slots at -45 and -15 minutes are used, +15 and +45 are not.
        DateTime startUtc = WholeSecond(DateTime.UtcNow.AddMinutes(-45));
        int id = await InsertReservationAsync(CustomerA, TableT01Id, startUtc, ReservationStatus.Seated);

        ServiceResult result = await _service.CompleteAsync(id);

        Assert.True(result.Succeeded);
        Assert.Equal(2, await SlotCountAsync(id));
    }

    [Fact]
    public async Task Complete_OrderNotPaidYet_IsRejectedAndStaysSeated()
    {
        int id = await InsertSeatedVisitAsync();
        await AddOrderAsync(id, OrderStatus.Completed, paid: false);

        ServiceResult result = await _service.CompleteAsync(id);

        Assert.False(result.Succeeded);
        Assert.Equal(ReservationService.UnpaidOrdersMessage, result.Errors[0]);
        Assert.Equal(ReservationStatus.Seated, await StatusOfAsync(id));
    }

    [Fact]
    public async Task Complete_PaidAndCancelledOrdersOnly_Succeeds()
    {
        int id = await InsertSeatedVisitAsync();
        await AddOrderAsync(id, OrderStatus.Completed, paid: true);
        await AddOrderAsync(id, OrderStatus.Cancelled, paid: false);

        ServiceResult result = await _service.CompleteAsync(id);

        Assert.True(result.Succeeded);
        Assert.Equal(ReservationStatus.Completed, await StatusOfAsync(id));
    }

    [Fact]
    public async Task CompleteAfterPayment_LastOrderPaid_CompletesTheVisit()
    {
        int id = await InsertSeatedVisitAsync();
        await AddOrderAsync(id, OrderStatus.Completed, paid: true);

        ServiceResult result = await _service.CompleteAfterPaymentAsync(id);

        Assert.True(result.Succeeded);
        Assert.Equal(ReservationStatus.Completed, await StatusOfAsync(id));
        Assert.Contains(CustomerA, _notifications.NotifiedUsers);
    }

    [Fact]
    public async Task CompleteAfterPayment_AnotherOrderStillUnpaid_StaysSeated()
    {
        int id = await InsertSeatedVisitAsync();
        await AddOrderAsync(id, OrderStatus.Completed, paid: true);
        await AddOrderAsync(id, OrderStatus.Served, paid: false);

        ServiceResult result = await _service.CompleteAfterPaymentAsync(id);

        Assert.True(result.Succeeded);
        Assert.Equal(ReservationStatus.Seated, await StatusOfAsync(id));
    }

    [Fact]
    public async Task CompleteAfterPayment_BookingNotSeatedYet_ChangesNothing()
    {
        // A pre-order paid before the guests arrive must not close the booking.
        DateTime startUtc = WholeSecond(DateTime.UtcNow.AddHours(2));
        int id = await InsertReservationAsync(CustomerA, TableT01Id, startUtc, ReservationStatus.Confirmed);
        await AddOrderAsync(id, OrderStatus.Completed, paid: true);

        ServiceResult result = await _service.CompleteAfterPaymentAsync(id);

        Assert.True(result.Succeeded);
        Assert.Equal(ReservationStatus.Confirmed, await StatusOfAsync(id));
    }

    [Fact]
    public async Task CancelByStaff_ConfirmedBooking_ReleasesTheTable()
    {
        int id = await BookAsync(CustomerA, SevenPm, TableT01Id);
        await _service.ConfirmAsync(id);

        ServiceResult result = await _service.CancelByStaffAsync(id);

        Assert.True(result.Succeeded);
        Assert.Equal(ReservationStatus.Cancelled, await StatusOfAsync(id));
        Assert.Equal(0, await SlotCountAsync(id));
        Assert.True(await IsOfferedAsync(TableT01Id, SevenPm));
    }

    [Fact]
    public async Task Cancel_SeatedBooking_IsRejected()
    {
        DateTime startUtc = WholeSecond(DateTime.UtcNow.AddMinutes(-10));
        int id = await InsertReservationAsync(CustomerA, TableT01Id, startUtc, ReservationStatus.Seated);

        ServiceResult result = await _service.CancelByStaffAsync(id);

        Assert.False(result.Succeeded);
        Assert.Equal(ReservationStatus.Seated, await StatusOfAsync(id));
    }

    [Fact]
    public async Task Seat_DaysBeforeTheBooking_IsRejectedAndTableStaysBooked()
    {
        // Booked a week ahead: staff must not be able to seat (and then complete) it today.
        int id = await BookAsync(CustomerA, SevenPm, TableT01Id);
        await _service.ConfirmAsync(id);

        ServiceResult result = await _service.SeatAsync(id);

        Assert.False(result.Succeeded);
        Assert.StartsWith("Guests can be seated from", result.Errors[0]);
        Assert.Equal(ReservationStatus.Confirmed, await StatusOfAsync(id));
        Assert.Equal(4, await SlotCountAsync(id));
    }

    [Fact]
    public async Task Seat_GuestsArriveTwentyMinutesEarly_IsAllowed()
    {
        DateTime startUtc = WholeSecond(DateTime.UtcNow.AddMinutes(20));
        int id = await InsertReservationAsync(CustomerA, TableT01Id, startUtc, ReservationStatus.Confirmed);

        ServiceResult result = await _service.SeatAsync(id);

        Assert.True(result.Succeeded);
        Assert.Equal(ReservationStatus.Seated, await StatusOfAsync(id));
    }

    [Fact]
    public async Task Seat_AfterTheVisitHasEnded_IsRejected()
    {
        // Started 3 hours ago, so the 2-hour visit is already over.
        DateTime startUtc = WholeSecond(DateTime.UtcNow.AddHours(-3));
        int id = await InsertReservationAsync(CustomerA, TableT01Id, startUtc, ReservationStatus.Confirmed);

        ServiceResult result = await _service.SeatAsync(id);

        Assert.False(result.Succeeded);
        Assert.Equal(ReservationStatus.Confirmed, await StatusOfAsync(id));
    }

    [Theory]
    [InlineData(-31, false)]
    [InlineData(-30, true)]
    [InlineData(0, true)]
    [InlineData(119, true)]
    [InlineData(120, false)]
    public void IsWithinSeatingTime_FromThirtyMinutesBeforeStartUntilTheEnd(int minutesFromStart, bool expected)
    {
        DateTime startUtc = new DateTime(2026, 10, 24, 11, 0, 0, DateTimeKind.Utc);
        DateTime endUtc = startUtc.AddMinutes(120);

        Assert.Equal(expected, ReservationService.IsWithinSeatingTime(startUtc, endUtc, startUtc.AddMinutes(minutesFromStart)));
    }

    [Fact]
    public async Task MarkNoShow_BeforeGracePeriodEnds_IsRejected()
    {
        int id = await BookAsync(CustomerA, SevenPm, TableT01Id);
        await _service.ConfirmAsync(id);

        ServiceResult result = await _service.MarkNoShowAsync(id);

        Assert.False(result.Succeeded);
        Assert.StartsWith("A booking can be marked as no-show", result.Errors[0]);
    }

    [Fact]
    public async Task MarkNoShow_AfterGracePeriod_ReleasesTheTable()
    {
        DateTime startUtc = WholeSecond(DateTime.UtcNow.AddMinutes(-(ReservationService.NoShowGraceMinutes + 5)));
        int id = await InsertReservationAsync(CustomerA, TableT01Id, startUtc, ReservationStatus.Confirmed);

        ServiceResult result = await _service.MarkNoShowAsync(id);

        Assert.True(result.Succeeded);
        Assert.Equal(ReservationStatus.NoShow, await StatusOfAsync(id));
        Assert.Equal(0, await SlotCountAsync(id));
    }

    [Fact]
    public async Task Confirm_MissingBooking_ReturnsNotFound()
    {
        ServiceResult result = await _service.ConfirmAsync(999);

        Assert.False(result.Succeeded);
        Assert.Contains(ReservationService.ReservationNotFoundMessage, result.Errors);
    }

    // ============================== 2. Customer: ownership and cancel ==============================//

    [Fact]
    public async Task CancelByCustomer_OwnBooking_SucceedsAndNotifiesStaff()
    {
        int id = await BookAsync(CustomerA, SevenPm, TableT01Id);
        _notifications.NotifiedRoles.Clear();

        ServiceResult result = await _service.CancelByCustomerAsync(CustomerA, id);

        Assert.True(result.Succeeded);
        Assert.Equal(ReservationStatus.Cancelled, await StatusOfAsync(id));
        Assert.Equal(RoleNames.Staff, Assert.Single(_notifications.NotifiedRoles));
    }

    [Fact]
    public async Task CancelByCustomer_SomeoneElsesBooking_IsNotFoundAndUnchanged()
    {
        int id = await BookAsync(CustomerA, SevenPm, TableT01Id);

        ServiceResult result = await _service.CancelByCustomerAsync(CustomerB, id);

        Assert.False(result.Succeeded);
        Assert.Contains(ReservationService.ReservationNotFoundMessage, result.Errors);
        Assert.Equal(ReservationStatus.Pending, await StatusOfAsync(id));
    }

    [Fact]
    public async Task CancelByCustomer_AfterStartTime_IsLocked()
    {
        DateTime startUtc = WholeSecond(DateTime.UtcNow.AddMinutes(-10));
        int id = await InsertReservationAsync(CustomerA, TableT01Id, startUtc, ReservationStatus.Confirmed);

        ServiceResult result = await _service.CancelByCustomerAsync(CustomerA, id);

        Assert.False(result.Succeeded);
        Assert.Contains(ReservationService.BookingLockedMessage, result.Errors);
    }

    [Fact]
    public async Task GetCustomerReservation_SomeoneElsesBooking_ReturnsNull()
    {
        int id = await BookAsync(CustomerA, SevenPm, TableT01Id);

        ReservationDetails? mine = await _service.GetCustomerReservationAsync(CustomerA, id);
        ReservationDetails? theirs = await _service.GetCustomerReservationAsync(CustomerB, id);

        Assert.NotNull(mine);
        Assert.Null(theirs);
    }

    // ============================== 3. Changing a booking ==============================//

    [Fact]
    public async Task ModifyByCustomer_SameTableHalfHourLater_MovesTheSlots()
    {
        int id = await BookAsync(CustomerA, SevenPm, TableT01Id);

        ServiceResult result = await _service.ModifyByCustomerAsync(CustomerA, id, Book(HalfPastSeven, TableT01Id));

        Assert.True(result.Succeeded);
        List<DateTime> slots = await _context.ReservationSlots.AsNoTracking()
            .Where(s => s.ReservationId == id)
            .OrderBy(s => s.SlotStartUtc)
            .Select(s => s.SlotStartUtc)
            .ToListAsync();
        Assert.Equal(4, slots.Count);
        Assert.Equal(ToUtc(HalfPastSeven), slots[0]);
    }

    [Fact]
    public async Task ModifyByCustomer_ConfirmedBooking_GoesBackToPending()
    {
        int id = await BookAsync(CustomerA, SevenPm, TableT01Id);
        await _service.ConfirmAsync(id);

        ServiceResult result = await _service.ModifyByCustomerAsync(CustomerA, id, Book(HalfPastSeven, TableT01Id));

        Assert.True(result.Succeeded);
        Assert.Equal(ReservationStatus.Pending, await StatusOfAsync(id));
    }

    [Fact]
    public async Task ModifyByStaff_ConfirmedBooking_StaysConfirmed()
    {
        int id = await BookAsync(CustomerA, SevenPm, TableT01Id);
        await _service.ConfirmAsync(id);

        ServiceResult result = await _service.ModifyByStaffAsync(id, Book(HalfPastSeven, TableT01Id));

        Assert.True(result.Succeeded);
        Assert.Equal(ReservationStatus.Confirmed, await StatusOfAsync(id));
    }

    [Fact]
    public async Task Modify_IntoAnotherBookingsTime_IsRejectedAndUnchanged()
    {
        await BookAsync(CustomerA, SevenPm, TableT01Id);
        int idB = await BookAsync(CustomerB, SevenPm, TableT02Id);

        // B tries to move onto T01, which A holds from 19:00 to 21:00.
        ServiceResult result = await _service.ModifyByCustomerAsync(CustomerB, idB, Book(HalfPastSeven, TableT01Id));

        Assert.False(result.Succeeded);
        Assert.Contains(ReservationService.TableJustTakenMessage, result.Errors);
        Reservation stillB = await _context.Reservations.AsNoTracking().SingleAsync(r => r.Id == idB);
        Assert.Equal(TableT02Id, stillB.DiningTableId);
    }

    [Fact]
    public async Task Modify_ToAnotherTable_ReleasesTheOldTable()
    {
        int id = await BookAsync(CustomerA, SevenPm, TableT01Id);

        ServiceResult result = await _service.ModifyByCustomerAsync(CustomerA, id, Book(SevenPm, TableT02Id));

        Assert.True(result.Succeeded);
        Assert.True(await IsOfferedAsync(TableT01Id, SevenPm));
        Assert.False(await IsOfferedAsync(TableT02Id, SevenPm));
    }

    [Fact]
    public async Task Search_IgnoringOwnBooking_OffersItsOwnTable()
    {
        int id = await BookAsync(CustomerA, SevenPm, TableT01Id);
        AvailabilitySearchInput search = new AvailabilitySearchInput { Date = VisitDate, Time = HalfPastSeven, PartySize = 2 };

        ServiceResult<IReadOnlyList<AvailableTable>> withoutIgnore = await _service.SearchAvailableTablesAsync(search);
        ServiceResult<IReadOnlyList<AvailableTable>> withIgnore = await _service.SearchAvailableTablesAsync(search, id);

        Assert.DoesNotContain(withoutIgnore.Value!, t => t.DiningTableId == TableT01Id);
        Assert.Contains(withIgnore.Value!, t => t.DiningTableId == TableT01Id);
    }

    // ============================== 4. Order check (YJ-3) and daily list (Darren) ==============================//

    [Theory]
    [InlineData(ReservationStatus.Pending, ReservationService.NotConfirmedYetMessage)]
    [InlineData(ReservationStatus.Confirmed, null)]
    [InlineData(ReservationStatus.Seated, null)]
    [InlineData(ReservationStatus.Completed, ReservationService.ClosedForOrdersMessage)]
    [InlineData(ReservationStatus.Cancelled, ReservationService.ClosedForOrdersMessage)]
    [InlineData(ReservationStatus.NoShow, ReservationService.ClosedForOrdersMessage)]
    public async Task CanOrder_DependsOnStatus(ReservationStatus status, string? expectedError)
    {
        DateTime startUtc = ToUtc(SevenPm);
        int id = await InsertReservationAsync(CustomerA, TableT01Id, startUtc, status);

        ServiceResult result = await _service.CanOrderForReservationAsync(CustomerA, id);

        if (expectedError == null)
        {
            Assert.True(result.Succeeded);
        }
        else
        {
            Assert.False(result.Succeeded);
            Assert.Contains(expectedError, result.Errors);
        }
    }

    [Fact]
    public async Task CanOrder_SomeoneElsesBooking_IsNotFound()
    {
        int id = await InsertReservationAsync(CustomerA, TableT01Id, ToUtc(SevenPm), ReservationStatus.Confirmed);

        ServiceResult result = await _service.CanOrderForReservationAsync(CustomerB, id);

        Assert.False(result.Succeeded);
        Assert.Contains(ReservationService.ReservationNotFoundMessage, result.Errors);
    }

    [Fact]
    public async Task GetReservationsForDate_ListsThatDayOnly_WithCustomerNames()
    {
        await BookAsync(CustomerA, SevenPm, TableT01Id);
        await BookAsync(CustomerB, SevenPm, TableT02Id);
        await _service.CreateReservationAsync(CustomerA, new BookingInput
        {
            Date = VisitDate.AddDays(1),
            Time = SevenPm,
            PartySize = 2,
            DiningTableId = TableT01Id
        });

        IReadOnlyList<ReservationDetails> day = await _service.GetReservationsForDateAsync(VisitDate);

        Assert.Equal(2, day.Count);
        Assert.Contains(day, r => r.CustomerName == CustomerAName);
        Assert.Contains(day, r => r.CustomerName == CustomerBName);
    }

    // ============================== Helpers ==============================//

    /// <summary>Books a table through the service for 2 guests on <see cref="VisitDate"/> and returns its ID.</summary>
    private async Task<int> BookAsync(string customerId, TimeOnly time, int tableId)
    {
        ServiceResult<int> result = await _service.CreateReservationAsync(customerId, Book(time, tableId));
        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        _context.ChangeTracker.Clear();
        return result.Value;
    }

    /// <summary>
    /// Inserts a booking directly, skipping the service's rules. Used for bookings that must already have started.
    /// </summary>
    private async Task<int> InsertReservationAsync(string customerId, int tableId, DateTime startUtc, ReservationStatus status)
    {
        DateTime nowUtc = DateTime.UtcNow;
        Reservation reservation = new Reservation
        {
            CustomerId = customerId,
            DiningTableId = tableId,
            PartySize = 2,
            StartUtc = startUtc,
            EndUtc = _timeRules.GetEndUtc(startUtc),
            Status = status,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        foreach (DateTime slotStart in _timeRules.GetSlotStartsUtc(startUtc))
        {
            reservation.Slots.Add(new ReservationSlot { DiningTableId = tableId, SlotStartUtc = slotStart });
        }

        _context.Reservations.Add(reservation);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return reservation.Id;
    }

    /// <summary>A Seated visit that started 30 minutes ago at T01 for customer A.</summary>
    private Task<int> InsertSeatedVisitAsync()
    {
        return InsertReservationAsync(CustomerA, TableT01Id, WholeSecond(DateTime.UtcNow.AddMinutes(-30)), ReservationStatus.Seated);
    }

    /// <summary>Adds an order (RM 20) to a booking, optionally with a Paid payment recorded by staff.</summary>
    private async Task AddOrderAsync(int reservationId, OrderStatus status, bool paid)
    {
        DateTime nowUtc = DateTime.UtcNow;
        Order order = new Order
        {
            ReservationId = reservationId,
            CustomerId = CustomerA,
            Status = status,
            TotalAmount = 20m,
            PlacedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        if (paid)
        {
            _context.Payments.Add(new Payment
            {
                OrderId = order.Id,
                Amount = order.TotalAmount,
                Method = PaymentMethod.Cash,
                Status = PaymentStatus.Paid,
                StaffId = CustomerB,
                PaidAtUtc = nowUtc
            });
            await _context.SaveChangesAsync();
        }

        _context.ChangeTracker.Clear();
    }

    /// <summary>The booking's status as stored in the database now.</summary>
    private async Task<ReservationStatus> StatusOfAsync(int reservationId)
    {
        _context.ChangeTracker.Clear();
        return await _context.Reservations.AsNoTracking()
            .Where(r => r.Id == reservationId)
            .Select(r => r.Status)
            .SingleAsync();
    }

    /// <summary>How many slots the booking still holds.</summary>
    private async Task<int> SlotCountAsync(int reservationId)
    {
        _context.ChangeTracker.Clear();
        return await _context.ReservationSlots.CountAsync(s => s.ReservationId == reservationId);
    }

    /// <summary>True when a search for 2 guests at this time on <see cref="VisitDate"/> offers the table.</summary>
    private async Task<bool> IsOfferedAsync(int tableId, TimeOnly time)
    {
        ServiceResult<IReadOnlyList<AvailableTable>> result = await _service.SearchAvailableTablesAsync(
            new AvailabilitySearchInput { Date = VisitDate, Time = time, PartySize = 2 });
        return result.Succeeded && result.Value!.Any(t => t.DiningTableId == tableId);
    }

    /// <summary>Booking form values for 2 guests on <see cref="VisitDate"/>.</summary>
    private static BookingInput Book(TimeOnly time, int tableId)
    {
        return new BookingInput { Date = VisitDate, Time = time, PartySize = 2, DiningTableId = tableId };
    }

    /// <summary>A Malaysia time on <see cref="VisitDate"/> as the UTC value the database stores.</summary>
    private static DateTime ToUtc(TimeOnly time)
    {
        return MalaysiaTime.ToUtc(VisitDate.ToDateTime(time));
    }

    /// <summary>Drops the fractions of a second, so stored and compared times match exactly.</summary>
    private static DateTime WholeSecond(DateTime value)
    {
        return new DateTime(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    }

    /// <summary>A minimal customer account row.</summary>
    private static ApplicationUser CreateCustomer(string id, string fullName)
    {
        return new ApplicationUser
        {
            Id = id,
            UserName = $"{id}@threegrills.test",
            Email = $"{id}@threegrills.test",
            FullName = fullName
        };
    }
}
