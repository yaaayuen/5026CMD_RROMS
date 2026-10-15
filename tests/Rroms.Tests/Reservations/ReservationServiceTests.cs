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
/// WL-2 acceptance: availability search and booking in <see cref="ReservationService"/>, including
/// the double-booking guarantee when two customers book at the same moment.
/// </summary>
/// <remarks>
/// Every test gets a fresh in-memory SQLite database with the WL-1 seed data: open every day 11:00-22:00,
/// tables T01 (2), T02 (2), T03 (4), T04 (4), T05 (6), T06 (8) in Main Hall and P01 (2), P02 (4) on the Patio.
/// Default rules: 120-minute visit, 30-minute slots, 90 days ahead, party 1-20.
/// Visits are booked a week ahead so the tests never depend on the time of day they run.
/// </remarks>
public class ReservationServiceTests : IDisposable
{
    /// <summary>Id of seeded table T01 (2 seats, Main Hall).</summary>
    private const int TableT01Id = 1;

    /// <summary>Id of seeded table T06 (8 seats, the largest).</summary>
    private const int TableT06Id = 6;

    /// <summary>Number of seeded tables.</summary>
    private const int SeededTableCount = 8;

    /// <summary>Seeded Main Hall area, needed when editing a table.</summary>
    private const int MainHallId = 1;

    private const string CustomerA = "customer-a";
    private const string CustomerB = "customer-b";

    private static readonly TimeOnly SevenPm = new TimeOnly(19, 0);
    private static readonly TimeOnly EightPm = new TimeOnly(20, 0);
    private static readonly TimeOnly NinePm = new TimeOnly(21, 0);
    private static readonly TimeOnly Noon = new TimeOnly(12, 0);
    private static readonly TimeOnly SeededOpensAt = new TimeOnly(11, 0);
    private static readonly TimeOnly SeededClosesAt = new TimeOnly(22, 0);

    /// <summary>Today in Malaysia.</summary>
    private static readonly DateOnly Today = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));

    /// <summary>A visit date one week ahead: always in the future and inside the 90-day window.</summary>
    private static readonly DateOnly VisitDate = Today.AddDays(7);

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly RecordingNotificationService _notifications;
    private readonly ReservationService _service;
    private readonly RestaurantService _restaurantService;

    /// <summary>Runs before EVERY test: a new database with the seed data and two customers.</summary>
    public ReservationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _context = CreateContext();
        _context.Database.EnsureCreated();

        // Reservations have a foreign key to AspNetUsers, so the customers must really exist.
        _context.Users.Add(CreateCustomer(CustomerA));
        _context.Users.Add(CreateCustomer(CustomerB));
        _context.SaveChanges();

        _notifications = new RecordingNotificationService();
        _restaurantService = new RestaurantService(_context);
        _service = CreateService(_context, _notifications);
    }

    /// <summary>Runs after EVERY test: closing the connection deletes the database.</summary>
    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ============================== 1. Start times ==============================//

    [Fact]
    public async Task GetStartTimes_OpenDay_ListsHalfHoursThatFinishByClosing()
    {
        IReadOnlyList<TimeOnly> times = await _service.GetStartTimesAsync(VisitDate);

        // Open 11:00-22:00 with a 2-hour visit: first start 11:00, last start 20:00, every 30 minutes = 19 times.
        Assert.Equal(19, times.Count);
        Assert.Equal(SeededOpensAt, times[0]);
        Assert.Equal(EightPm, times[^1]);
    }

    [Fact]
    public async Task GetStartTimes_WeekdayMarkedClosed_IsEmpty()
    {
        await _restaurantService.UpdateOperatingHourAsync(new OperatingHourInput
        {
            DayOfWeek = VisitDate.DayOfWeek,
            OpensAt = SeededOpensAt,
            ClosesAt = SeededClosesAt,
            IsClosed = true
        });

        IReadOnlyList<TimeOnly> times = await _service.GetStartTimesAsync(VisitDate);

        Assert.Empty(times);
    }

    [Fact]
    public async Task GetStartTimes_SpecialClosure_IsEmpty()
    {
        await _restaurantService.AddSpecialClosureAsync(new SpecialClosureInput
        {
            StartDate = VisitDate,
            EndDate = VisitDate,
            Reason = "Private event"
        });

        IReadOnlyList<TimeOnly> times = await _service.GetStartTimesAsync(VisitDate);

        Assert.Empty(times);
    }

    // ============================== 2. Search ==============================//

    [Fact]
    public async Task Search_PartyOfTwo_ReturnsEveryTableSmallestFirst()
    {
        ServiceResult<IReadOnlyList<AvailableTable>> result = await _service.SearchAvailableTablesAsync(Search(VisitDate, SevenPm, 2));

        Assert.True(result.Succeeded);
        Assert.Equal(SeededTableCount, result.Value!.Count);
        Assert.Equal(2, result.Value[0].Capacity);
    }

    [Fact]
    public async Task Search_PartyOfSeven_OnlyTheEightSeatTableFits()
    {
        ServiceResult<IReadOnlyList<AvailableTable>> result = await _service.SearchAvailableTablesAsync(Search(VisitDate, SevenPm, 7));

        Assert.True(result.Succeeded);
        AvailableTable only = Assert.Single(result.Value!);
        Assert.Equal(TableT06Id, only.DiningTableId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Search_PartySizeOutsideLimits_IsRejected(int partySize)
    {
        ServiceResult<IReadOnlyList<AvailableTable>> result = await _service.SearchAvailableTablesAsync(Search(VisitDate, SevenPm, partySize));

        Assert.False(result.Succeeded);
        Assert.StartsWith("Party size must be between", result.Errors[0]);
    }

    [Fact]
    public async Task Search_TimeOffTheHalfHour_IsRejected()
    {
        ServiceResult<IReadOnlyList<AvailableTable>> result = await _service.SearchAvailableTablesAsync(Search(VisitDate, new TimeOnly(19, 15), 2));

        Assert.False(result.Succeeded);
        Assert.Contains(ReservationService.OffGridMessage, result.Errors);
    }

    [Fact]
    public async Task Search_YesterdayEvening_IsRejectedAsPast()
    {
        ServiceResult<IReadOnlyList<AvailableTable>> result = await _service.SearchAvailableTablesAsync(Search(Today.AddDays(-1), SevenPm, 2));

        Assert.False(result.Succeeded);
        Assert.Contains(ReservationService.PastTimeMessage, result.Errors);
    }

    [Fact]
    public async Task Search_BeyondNinetyDays_IsRejected()
    {
        ReservationRulesOptions defaults = new ReservationRulesOptions();

        ServiceResult<IReadOnlyList<AvailableTable>> result = await _service.SearchAvailableTablesAsync(
            Search(Today.AddDays(defaults.MaxDaysAhead + 1), SevenPm, 2));

        Assert.False(result.Succeeded);
        Assert.StartsWith("Bookings can be made up to", result.Errors[0]);
    }

    [Fact]
    public async Task Search_VisitWouldEndAfterClosing_IsRejected()
    {
        // 21:00 + 2 hours = 23:00, but the restaurant closes at 22:00.
        ServiceResult<IReadOnlyList<AvailableTable>> result = await _service.SearchAvailableTablesAsync(Search(VisitDate, NinePm, 2));

        Assert.False(result.Succeeded);
        Assert.Contains(ReservationService.OutsideHoursMessage, result.Errors);
    }

    [Fact]
    public async Task Search_SpecialClosure_IsRejectedAsClosed()
    {
        await _restaurantService.AddSpecialClosureAsync(new SpecialClosureInput
        {
            StartDate = VisitDate,
            EndDate = VisitDate,
            Reason = "Renovation"
        });

        ServiceResult<IReadOnlyList<AvailableTable>> result = await _service.SearchAvailableTablesAsync(Search(VisitDate, SevenPm, 2));

        Assert.False(result.Succeeded);
        Assert.Contains(ReservationService.ClosedMessage, result.Errors);
    }

    [Fact]
    public async Task Search_TableBookedForAnOverlappingTime_IsNotOffered()
    {
        await _service.CreateReservationAsync(CustomerA, Book(VisitDate, SevenPm, 2, TableT01Id));

        // 20:00-22:00 overlaps the 19:00-21:00 booking.
        ServiceResult<IReadOnlyList<AvailableTable>> result = await _service.SearchAvailableTablesAsync(Search(VisitDate, EightPm, 2));

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(result.Value!, t => t.DiningTableId == TableT01Id);
        Assert.Equal(SeededTableCount - 1, result.Value!.Count);
    }

    [Fact]
    public async Task Search_BackToBackBooking_TableIsOfferedAgain()
    {
        await _service.CreateReservationAsync(CustomerA, Book(VisitDate, SevenPm, 2, TableT01Id));

        // 21:00 starts exactly when the 19:00-21:00 booking ends, so they do not overlap.
        // A 21:00 visit ends at 23:00, so keep the restaurant open until 23:00 for this test.
        await _restaurantService.UpdateOperatingHourAsync(new OperatingHourInput
        {
            DayOfWeek = VisitDate.DayOfWeek,
            OpensAt = SeededOpensAt,
            ClosesAt = new TimeOnly(23, 0),
            IsClosed = false
        });

        ServiceResult<IReadOnlyList<AvailableTable>> result = await _service.SearchAvailableTablesAsync(Search(VisitDate, NinePm, 2));

        Assert.True(result.Succeeded);
        Assert.Contains(result.Value!, t => t.DiningTableId == TableT01Id);
    }

    // ============================== 3. Booking ==============================//

    [Fact]
    public async Task Create_ValidBooking_IsPendingHoldsFourSlotsAndNotifiesStaff()
    {
        ServiceResult<int> result = await _service.CreateReservationAsync(CustomerA, Book(VisitDate, SevenPm, 2, TableT01Id));

        Assert.True(result.Succeeded);
        Reservation saved = await _context.Reservations
            .Include(r => r.Slots)
            .AsNoTracking()
            .SingleAsync(r => r.Id == result.Value);
        Assert.Equal(ReservationStatus.Pending, saved.Status);
        Assert.Equal(4, saved.Slots.Count);
        Assert.Equal(saved.StartUtc.AddMinutes(120), saved.EndUtc);
        Assert.Equal(RoleNames.Staff, Assert.Single(_notifications.NotifiedRoles));
    }

    [Fact]
    public async Task Create_SameTableOverlappingTime_IsRejected()
    {
        await _service.CreateReservationAsync(CustomerA, Book(VisitDate, SevenPm, 2, TableT01Id));

        ServiceResult<int> second = await _service.CreateReservationAsync(CustomerB, Book(VisitDate, EightPm, 2, TableT01Id));

        Assert.False(second.Succeeded);
        Assert.Contains(ReservationService.TableJustTakenMessage, second.Errors);
        Assert.Equal(1, await _context.Reservations.CountAsync());
    }

    [Fact]
    public async Task Create_PartyTooLargeForTable_IsRejected()
    {
        // T01 has 2 seats.
        ServiceResult<int> result = await _service.CreateReservationAsync(CustomerA, Book(VisitDate, SevenPm, 4, TableT01Id));

        Assert.False(result.Succeeded);
        Assert.Contains(ReservationService.TableUnavailableMessage, result.Errors);
    }

    [Fact]
    public async Task Create_InactiveTable_IsRejected()
    {
        await _restaurantService.UpdateTableAsync(TableT01Id, new DiningTableInput
        {
            Label = "T01",
            Capacity = 2,
            DiningAreaId = MainHallId,
            IsActive = false
        });

        ServiceResult<int> result = await _service.CreateReservationAsync(CustomerA, Book(VisitDate, SevenPm, 2, TableT01Id));

        Assert.False(result.Succeeded);
        Assert.Contains(ReservationService.TableUnavailableMessage, result.Errors);
    }

    [Fact]
    public async Task Create_TwoCustomersAtTheSameMoment_DatabaseLetsOnlyOneThrough()
    {
        // Customer A uses a context with the interceptor; customer B uses a separate context,
        // like two web requests. Both share the same database.
        BeforeSaveInterceptor interceptor = new BeforeSaveInterceptor();
        using ApplicationDbContext contextA = CreateContext(interceptor);
        using ApplicationDbContext contextB = CreateContext();
        ReservationService serviceA = CreateService(contextA, new RecordingNotificationService());
        ReservationService serviceB = CreateService(contextB, new RecordingNotificationService());

        ServiceResult<int>? resultB = null;

        // A's C# check has already passed when its save starts. In that gap, B books the same table and time.
        interceptor.RunOnceBeforeNextSave(async () =>
        {
            resultB = await serviceB.CreateReservationAsync(CustomerB, Book(VisitDate, SevenPm, 2, TableT01Id));
        });

        ServiceResult<int> resultA = await serviceA.CreateReservationAsync(CustomerA, Book(VisitDate, SevenPm, 2, TableT01Id));

        // B saved first and won. A passed the C# check but the unique index rejected its save.
        Assert.NotNull(resultB);
        Assert.True(resultB!.Succeeded);
        Assert.False(resultA.Succeeded);
        Assert.Contains(ReservationService.TableJustTakenMessage, resultA.Errors);

        using ApplicationDbContext checkContext = CreateContext();
        Assert.Equal(1, await checkContext.Reservations.CountAsync());
        Assert.Equal(4, await checkContext.ReservationSlots.CountAsync());
    }

    [Fact]
    public async Task Database_DuplicateSlotThatBypassesTheService_IsBlockedByUniqueIndex()
    {
        ServiceResult<int> booked = await _service.CreateReservationAsync(CustomerA, Book(VisitDate, SevenPm, 2, TableT01Id));
        ReservationSlot existing = await _context.ReservationSlots.AsNoTracking().FirstAsync();

        // Insert a second row for the same table and time directly, skipping every C# check.
        _context.ReservationSlots.Add(new ReservationSlot
        {
            ReservationId = booked.Value,
            DiningTableId = existing.DiningTableId,
            SlotStartUtc = existing.SlotStartUtc
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    // ============================== 4. My reservations ==============================//

    [Fact]
    public async Task GetCustomerReservations_OnlyOwnBookings_NewestFirst()
    {
        await _service.CreateReservationAsync(CustomerA, Book(VisitDate, SevenPm, 2, TableT01Id));
        await _service.CreateReservationAsync(CustomerA, Book(VisitDate.AddDays(1), Noon, 2, TableT01Id));
        await _service.CreateReservationAsync(CustomerB, Book(VisitDate, SevenPm, 4, TableT06Id));

        IReadOnlyList<ReservationSummary> mine = await _service.GetCustomerReservationsAsync(CustomerA);

        Assert.Equal(2, mine.Count);
        Assert.True(mine[0].StartUtc > mine[1].StartUtc);
        Assert.Equal("T01", mine[0].TableLabel);
        Assert.Equal("Main Hall", mine[0].AreaName);
    }

    // ============================== Helpers ==============================//

    /// <summary>Creates a context on the shared in-memory database, optionally with interceptors.</summary>
    private ApplicationDbContext CreateContext(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .Options;

        return new ApplicationDbContext(options);
    }

    /// <summary>Builds a ReservationService the same way dependency injection does, with the default rules.</summary>
    private static ReservationService CreateService(ApplicationDbContext context, RecordingNotificationService notifications)
    {
        Microsoft.Extensions.Options.IOptions<ReservationRulesOptions> rules =
            Microsoft.Extensions.Options.Options.Create(new ReservationRulesOptions());

        return new ReservationService(
            context,
            new RestaurantService(context),
            new ReservationTimeRules(rules),
            rules,
            notifications,
            new OrderProcessingService(context, notifications));
    }

    /// <summary>A minimal customer account row.</summary>
    private static ApplicationUser CreateCustomer(string id)
    {
        return new ApplicationUser
        {
            Id = id,
            UserName = $"{id}@threegrills.test",
            Email = $"{id}@threegrills.test",
            FullName = "Test Customer"
        };
    }

    /// <summary>Search form values.</summary>
    private static AvailabilitySearchInput Search(DateOnly date, TimeOnly time, int partySize)
    {
        return new AvailabilitySearchInput { Date = date, Time = time, PartySize = partySize };
    }

    /// <summary>Booking form values.</summary>
    private static BookingInput Book(DateOnly date, TimeOnly time, int partySize, int tableId)
    {
        return new BookingInput { Date = date, Time = time, PartySize = partySize, DiningTableId = tableId };
    }
}
