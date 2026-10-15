using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
using Rroms.Web.Services;

namespace Rroms.Tests.Analytics;

/// <summary>
/// WL-4 acceptance: utilisation %, peak hours heatmap, daily trend and upcoming confirmed bookings,
/// checked against figures worked out by hand.
/// </summary>
/// <remarks>
/// Fresh in-memory database per test with the WL-1 seed data: open 11:00–22:00 every day (11 hours)
/// and 8 bookable tables, so one open day offers 11 × 8 = 88 table-hours.
///
/// The test week is Monday 5 – Sunday 11 October 2026 (Malaysia time), in the past, so bookings are
/// inserted directly with the slots they would still hold:
/// <list type="table">
/// <item>A  Mon 19:00  T01  2 guests  Completed  4 slots (19:00–21:00)  → 2.0 h used</item>
/// <item>B  Tue 12:00  T03  4 guests  Completed, left early: 2 slots (12:00–13:00)  → 1.0 h used</item>
/// <item>C  Mon 19:00  T02  3 guests  Cancelled  0 slots</item>
/// <item>D  Wed 18:00  T04  2 guests  NoShow     0 slots</item>
/// <item>E  Thu 19:00  T05  5 guests  Pending    4 slots  → not counted (never confirmed)</item>
/// <item>F  Fri 20:00  T06  6 guests  Confirmed  4 slots  → not counted (not really used)</item>
/// </list>
/// Week: used 3.0 h, available 7 × 88 = 616 h, utilisation 3.0 ÷ 616 = 0.487% → 0.5%.
/// Monday only: used 2.0 h, available 88 h, utilisation 2.27% → 2.3%.
/// </remarks>
public class AnalyticsServiceTests : IDisposable
{
    private const int TableT01Id = 1;
    private const int TableT02Id = 2;
    private const int TableT03Id = 3;
    private const int TableT04Id = 4;
    private const int TableT05Id = 5;
    private const int TableT06Id = 6;

    private const string CustomerA = "customer-a";

    private static readonly DateOnly Monday = new DateOnly(2026, 10, 5);
    private static readonly DateOnly Sunday = new DateOnly(2026, 10, 11);

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly ReservationTimeRules _timeRules;
    private readonly AnalyticsService _service;

    /// <summary>Runs before EVERY test: a new database with the seed data and one customer.</summary>
    public AnalyticsServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _context.Users.Add(new ApplicationUser
        {
            Id = CustomerA,
            UserName = "customer-a@threegrills.test",
            Email = "customer-a@threegrills.test",
            FullName = "Alice Tan"
        });
        _context.SaveChanges();

        Microsoft.Extensions.Options.IOptions<ReservationRulesOptions> rules =
            Microsoft.Extensions.Options.Options.Create(new ReservationRulesOptions());
        _timeRules = new ReservationTimeRules(rules);
        _service = new AnalyticsService(_context, new RestaurantService(_context), _timeRules);
    }

    /// <summary>Runs after EVERY test: closing the connection deletes the database.</summary>
    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ============================== 1. Utilisation ==============================//

    /// <summary>Monday alone: only booking A's 4 slots count (C was cancelled), 2.0 of 88 table-hours.</summary>
    [Fact]
    public async Task Utilisation_OneDay_CountsOnlySeatedAndCompletedSlots()
    {
        await AddTestWeekAsync();

        OccupancyReport report = await GetReportAsync(Monday, Monday);

        Assert.Equal(2.0, report.OccupiedTableHours);
        Assert.Equal(88.0, report.AvailableTableHours);
        Assert.Equal(2.3, report.UtilisationPercent);
    }

    /// <summary>The whole week: A (2.0 h) + B left early (1.0 h); Pending E and Confirmed F are not counted.</summary>
    [Fact]
    public async Task Utilisation_Week_MatchesHandCalculation()
    {
        await AddTestWeekAsync();

        OccupancyReport report = await GetReportAsync(Monday, Sunday);

        Assert.Equal(3.0, report.OccupiedTableHours);
        Assert.Equal(616.0, report.AvailableTableHours);
        Assert.Equal(0.5, report.UtilisationPercent);
    }

    /// <summary>A special closure on Wednesday removes that day's 88 table-hours.</summary>
    [Fact]
    public async Task Available_SpecialClosureDay_IsNotCounted()
    {
        await AddClosureAsync(Monday.AddDays(2), Monday.AddDays(2));

        OccupancyReport report = await GetReportAsync(Monday, Sunday);

        Assert.Equal(6 * 88.0, report.AvailableTableHours);
    }

    /// <summary>When every day is closed there is nothing to divide by: utilisation is 0, not NaN.</summary>
    [Fact]
    public async Task Utilisation_EveryDayClosed_IsZeroNotNaN()
    {
        await AddClosureAsync(Monday, Sunday);

        OccupancyReport report = await GetReportAsync(Monday, Sunday);

        Assert.Equal(0.0, report.AvailableTableHours);
        Assert.Equal(0.0, report.UtilisationPercent);
    }

    // ============================== 2. Peak hours heatmap ==============================//

    /// <summary>
    /// A's slots (Mon 19:00–21:00 Malaysia) land in Monday 19 and 20; B's (Tue 12:00–13:00) in Tuesday 12.
    /// Monday 11 stays empty, which proves the slots were converted from UTC (19:00 Malaysia = 11:00 UTC).
    /// </summary>
    [Fact]
    public async Task Heatmap_PlacesSlotsInMalaysiaTime()
    {
        await AddTestWeekAsync();

        OccupancyHeatmap heatmap = (await GetReportAsync(Monday, Sunday)).Heatmap;

        Assert.Equal(Enumerable.Range(11, 11), heatmap.Hours);
        Assert.Equal(DayOfWeek.Monday, heatmap.Days[0]);
        Assert.Equal(1.0, Cell(heatmap, DayOfWeek.Monday, 19));
        Assert.Equal(1.0, Cell(heatmap, DayOfWeek.Monday, 20));
        Assert.Equal(1.0, Cell(heatmap, DayOfWeek.Tuesday, 12));
        Assert.Equal(0.0, Cell(heatmap, DayOfWeek.Monday, 11));
        Assert.Equal(1.0, heatmap.MaxCell);
    }

    /// <summary>The heatmap and the utilisation use the same slots, so its cells add up to the occupied hours.</summary>
    [Fact]
    public async Task Heatmap_CellsAddUpToOccupiedHours()
    {
        await AddTestWeekAsync();

        OccupancyReport report = await GetReportAsync(Monday, Sunday);

        double total = report.Heatmap.Cells.SelectMany(row => row).Sum();
        Assert.Equal(report.OccupiedTableHours, total);
    }

    // ============================== 3. Daily trend ==============================//

    /// <summary>One row per day, quiet days included, with each booking counted under its status.</summary>
    [Fact]
    public async Task Trend_ListsEveryDayWithStatusCounts()
    {
        await AddTestWeekAsync();

        IReadOnlyList<DailyReservationCount> trend = (await GetReportAsync(Monday, Sunday)).DailyTrend;

        Assert.Equal(7, trend.Count);
        Assert.Equal(Monday, trend[0].Date);

        DailyReservationCount monday = trend[0];
        Assert.Equal(1, monday.Completed);
        Assert.Equal(1, monday.Cancelled);
        Assert.Equal(2, monday.Total);
        Assert.Equal(2, monday.GuestsServed);

        Assert.Equal(4, trend[1].GuestsServed);   // Tuesday: B, 4 guests
        Assert.Equal(1, trend[2].NoShow);         // Wednesday: D
        Assert.Equal(1, trend[3].Pending);        // Thursday: E
        Assert.Equal(1, trend[4].Confirmed);      // Friday: F
        Assert.Equal(0, trend[5].Total);          // Saturday: no bookings, but still listed
        Assert.Equal(0, trend[6].Total);          // Sunday
    }

    // ============================== 4. Upcoming confirmed ==============================//

    /// <summary>Only Confirmed bookings in the next 7 days are counted; one 10 days away and a Pending one are not.</summary>
    [Fact]
    public async Task Upcoming_CountsConfirmedBookingsInTheNextSevenDays()
    {
        DateTime tomorrowUtc = WholeSecond(DateTime.UtcNow.AddDays(1));
        await InsertReservationAsync(TableT01Id, tomorrowUtc, ReservationStatus.Confirmed, partySize: 3);
        await InsertReservationAsync(TableT02Id, tomorrowUtc, ReservationStatus.Pending, partySize: 2);
        await InsertReservationAsync(TableT03Id, tomorrowUtc.AddDays(9), ReservationStatus.Confirmed, partySize: 5);

        OccupancyReport report = await GetReportAsync(Monday, Sunday);

        Assert.Equal(1, report.UpcomingConfirmedBookings);
        Assert.Equal(3, report.UpcomingConfirmedGuests);
        Assert.Equal(AnalyticsService.UpcomingDays, report.UpcomingDays);
    }

    // ============================== 5. Range validation ==============================//

    /// <summary>An end date before the start date is refused with a clear message.</summary>
    [Fact]
    public async Task Range_EndBeforeStart_IsRejected()
    {
        ServiceResult<OccupancyReport> result = await _service.GetOccupancyReportAsync(Sunday, Monday);

        Assert.False(result.Succeeded);
        Assert.Contains(AnalyticsService.EndBeforeStartMessage, result.Errors);
    }

    /// <summary>A range longer than a year is refused, so a typo cannot start a huge calculation.</summary>
    [Fact]
    public async Task Range_LongerThanMaximum_IsRejected()
    {
        ServiceResult<OccupancyReport> result = await _service.GetOccupancyReportAsync(
            Monday, Monday.AddDays(AnalyticsService.MaxRangeDays));

        Assert.False(result.Succeeded);
        Assert.Contains(AnalyticsService.RangeTooLongMessage, result.Errors);
    }

    // ============================== Helpers ==============================//

    /// <summary>Adds bookings A–F from the class remarks.</summary>
    private async Task AddTestWeekAsync()
    {
        await InsertReservationAsync(TableT01Id, Utc(Monday, 19), ReservationStatus.Completed, partySize: 2);
        await InsertReservationAsync(TableT03Id, Utc(Monday.AddDays(1), 12), ReservationStatus.Completed, partySize: 4, keepSlots: 2);
        await InsertReservationAsync(TableT02Id, Utc(Monday, 19), ReservationStatus.Cancelled, partySize: 3, keepSlots: 0);
        await InsertReservationAsync(TableT04Id, Utc(Monday.AddDays(2), 18), ReservationStatus.NoShow, partySize: 2, keepSlots: 0);
        await InsertReservationAsync(TableT05Id, Utc(Monday.AddDays(3), 19), ReservationStatus.Pending, partySize: 5);
        await InsertReservationAsync(TableT06Id, Utc(Monday.AddDays(4), 20), ReservationStatus.Confirmed, partySize: 6);
    }

    /// <summary>
    /// Inserts a booking directly, holding its first <paramref name="keepSlots"/> slots
    /// (all 4 by default; fewer for an early Complete; 0 for Cancelled or NoShow, as WL-3 releases them).
    /// </summary>
    private async Task InsertReservationAsync(int tableId, DateTime startUtc, ReservationStatus status, int partySize, int? keepSlots = null)
    {
        Reservation reservation = new Reservation
        {
            CustomerId = CustomerA,
            DiningTableId = tableId,
            PartySize = partySize,
            StartUtc = startUtc,
            EndUtc = _timeRules.GetEndUtc(startUtc),
            Status = status,
            CreatedAtUtc = startUtc.AddDays(-3),
            UpdatedAtUtc = startUtc
        };

        IReadOnlyList<DateTime> slots = _timeRules.GetSlotStartsUtc(startUtc);
        foreach (DateTime slotStart in slots.Take(keepSlots ?? slots.Count))
        {
            reservation.Slots.Add(new ReservationSlot { DiningTableId = tableId, SlotStartUtc = slotStart });
        }

        _context.Reservations.Add(reservation);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    /// <summary>Adds a special closure through the WL-1 service.</summary>
    private async Task AddClosureAsync(DateOnly start, DateOnly end)
    {
        ServiceResult<int> result = await new RestaurantService(_context).AddSpecialClosureAsync(new SpecialClosureInput
        {
            StartDate = start,
            EndDate = end,
            Reason = "Test closure"
        });
        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
    }

    /// <summary>Runs the report and fails the test straight away if the service refused it.</summary>
    private async Task<OccupancyReport> GetReportAsync(DateOnly from, DateOnly to)
    {
        ServiceResult<OccupancyReport> result = await _service.GetOccupancyReportAsync(from, to);
        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        return result.Value!;
    }

    /// <summary>One heatmap cell by weekday and hour.</summary>
    private static double Cell(OccupancyHeatmap heatmap, DayOfWeek day, int hour)
    {
        int row = heatmap.Days.ToList().IndexOf(day);
        int column = heatmap.Hours.ToList().IndexOf(hour);
        return heatmap.Cells[row][column];
    }

    /// <summary>A Malaysia date and hour as UTC.</summary>
    private static DateTime Utc(DateOnly date, int hour)
    {
        return MalaysiaTime.ToUtc(date.ToDateTime(new TimeOnly(hour, 0)));
    }

    /// <summary>Drops milliseconds, so the value survives the round trip through SQLite unchanged.</summary>
    private static DateTime WholeSecond(DateTime value)
    {
        return new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, value.Kind);
    }
}
