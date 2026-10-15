using Microsoft.EntityFrameworkCore;

using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Calculates table occupancy and reservation analytics (WL-4).
/// </summary>
/// <remarks>
/// Formula: utilisation % = occupied table-hours ÷ available table-hours × 100.
/// <list type="bullet">
/// <item>Occupied table-hours = ReservationSlot rows of Seated or Completed bookings × slot length (0.5 hour).
/// Cancelled and no-show bookings have no slots left, and an early Complete keeps only the slots really used (WL-3).</item>
/// <item>Available table-hours = for every day the restaurant is open: opening hours × bookable tables
/// (active tables in active areas). Closed weekdays and special closures count as 0.</item>
/// </list>
/// Limitation: available hours use today's tables and opening hours, because their history is not stored.
/// </remarks>
public class AnalyticsService : IAnalyticsService
{
    /// <summary>Longest range allowed in one report, so a mistyped year cannot make a huge calculation.</summary>
    public const int MaxRangeDays = 366;

    /// <summary>How many days ahead the upcoming Confirmed figures look.</summary>
    public const int UpcomingDays = 7;

    /// <summary>Error when the end date is before the start date.</summary>
    public const string EndBeforeStartMessage = "The end date must be on or after the start date.";

    /// <summary>Error when the range is longer than <see cref="MaxRangeDays"/>.</summary>
    public static readonly string RangeTooLongMessage = $"Please choose a range of at most {MaxRangeDays} days.";

    /// <summary>Statuses that mean the table was really used.</summary>
    private static readonly ReservationStatus[] OccupiedStatuses =
    {
        ReservationStatus.Seated,
        ReservationStatus.Completed
    };

    private readonly ApplicationDbContext _context;
    private readonly IRestaurantService _restaurantService;
    private readonly ReservationTimeRules _timeRules;

    /// <summary>Receives its dependencies from dependency injection.</summary>
    /// <param name="context">The database.</param>
    /// <param name="restaurantService">Opening hours, special closures and bookable tables (WL-1).</param>
    /// <param name="timeRules">Slot length, so one slot row can be turned into hours (WL-2).</param>
    public AnalyticsService(ApplicationDbContext context, IRestaurantService restaurantService, ReservationTimeRules timeRules)
    {
        _context = context;
        _restaurantService = restaurantService;
        _timeRules = timeRules;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<OccupancyReport>> GetOccupancyReportAsync(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return ServiceResult<OccupancyReport>.Failure(EndBeforeStartMessage);
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
        {
            return ServiceResult<OccupancyReport>.Failure(RangeTooLongMessage);
        }

        // The range is in Malaysia dates; the database stores UTC. "to" is included, so we stop at midnight after it.
        DateTime fromUtc = MalaysiaTime.ToUtc(from.ToDateTime(TimeOnly.MinValue));
        DateTime toUtcExclusive = MalaysiaTime.ToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue));

        double occupied = await GetOccupiedTableHoursAsync(fromUtc, toUtcExclusive);
        double available = await GetAvailableTableHoursAsync(from, to);

        OccupancyReport report = new OccupancyReport
        {
            From = from,
            To = to,
            OccupiedTableHours = occupied,
            AvailableTableHours = available,
            UtilisationPercent = available > 0 ? Math.Round(occupied / available * 100, 1) : 0,
            UpcomingDays = UpcomingDays
        };

        await AddUpcomingConfirmedAsync(report);
        return ServiceResult<OccupancyReport>.Success(report);
    }

    // ============================== Helpers ==============================//

    /// <summary>Counts the slots of Seated or Completed bookings in the range and turns them into hours.</summary>
    private async Task<double> GetOccupiedTableHoursAsync(DateTime fromUtc, DateTime toUtcExclusive)
    {
        int slotCount = await _context.ReservationSlots
            .Where(s => s.SlotStartUtc >= fromUtc && s.SlotStartUtc < toUtcExclusive
                && OccupiedStatuses.Contains(s.Reservation!.Status))
            .CountAsync();

        return slotCount * _timeRules.SlotLength.TotalHours;
    }

    /// <summary>Adds up opening hours × bookable tables for every open day in the range.</summary>
    private async Task<double> GetAvailableTableHoursAsync(DateOnly from, DateOnly to)
    {
        IReadOnlyList<OperatingHour> hours = await _restaurantService.GetOperatingHoursAsync();
        IReadOnlyList<SpecialClosure> closures = await _restaurantService.GetSpecialClosuresAsync();
        int tableCount = (await _restaurantService.GetBookableTablesAsync()).Count;

        double total = 0;
        for (DateOnly day = from; day <= to; day = day.AddDays(1))
        {
            OperatingHour? today = hours.FirstOrDefault(h => h.DayOfWeek == day.DayOfWeek);
            bool specialClosure = closures.Any(c => c.StartDate <= day && day <= c.EndDate);
            if (today == null || today.IsClosed || specialClosure)
            {
                continue;
            }

            total += (today.ClosesAt - today.OpensAt).TotalHours * tableCount;
        }

        return total;
    }

    /// <summary>Counts Confirmed bookings (and their guests) starting from now until <see cref="UpcomingDays"/> days ahead.</summary>
    private async Task AddUpcomingConfirmedAsync(OccupancyReport report)
    {
        DateTime nowUtc = DateTime.UtcNow;
        DateTime untilUtc = nowUtc.AddDays(UpcomingDays);

        List<int> partySizes = await _context.Reservations
            .Where(r => r.Status == ReservationStatus.Confirmed && r.StartUtc >= nowUtc && r.StartUtc < untilUtc)
            .Select(r => r.PartySize)
            .ToListAsync();

        report.UpcomingConfirmedBookings = partySizes.Count;
        report.UpcomingConfirmedGuests = partySizes.Sum();
    }
}
