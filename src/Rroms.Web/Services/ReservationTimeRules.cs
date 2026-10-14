using Microsoft.Extensions.Options;

using Rroms.Web.Options;

namespace Rroms.Web.Services;

/// <summary>
/// The single place where booking time rules are calculated (WL-2): visit length, slot grid,
/// which slots a booking holds, and when two bookings overlap.
/// </summary>
/// <remarks>
/// Booking, availability search and analytics (WL-5) all use this class, and every number comes from
/// <see cref="ReservationRulesOptions"/>. So "make visits 90 minutes" is one edit in appsettings.json,
/// and "add a cleaning buffer" is one edit in <see cref="BlockedLength"/>.
/// It has no database access, so tests create it directly without an interface.
/// </remarks>
public class ReservationTimeRules
{
    private readonly ReservationRulesOptions _options;

    /// <summary>Receives the booking rules from configuration.</summary>
    /// <param name="options">The "ReservationRules" section of appsettings.json.</param>
    /// <exception cref="InvalidOperationException">The configuration has a zero or negative length.</exception>
    public ReservationTimeRules(IOptions<ReservationRulesOptions> options)
    {
        _options = options.Value;

        // A zero slot length would cause a division by zero later, so fail at start-up with a clear message.
        if (_options.SlotIntervalMinutes <= 0 || _options.VisitDurationMinutes <= 0)
        {
            throw new InvalidOperationException(
                "ReservationRules: VisitDurationMinutes and SlotIntervalMinutes must be greater than zero.");
        }
    }

    /// <summary>How long a customer has the table, e.g. 120 minutes.</summary>
    public TimeSpan VisitLength
    {
        get { return TimeSpan.FromMinutes(_options.VisitDurationMinutes); }
    }

    /// <summary>Length of one slot on the booking grid, e.g. 30 minutes.</summary>
    public TimeSpan SlotLength
    {
        get { return TimeSpan.FromMinutes(_options.SlotIntervalMinutes); }
    }

    /// <summary>
    /// How long a booking keeps the table away from other customers. Today this equals the visit length.
    /// A cleaning buffer between bookings would be added here, e.g. VisitLength + TimeSpan.FromMinutes(15).
    /// </summary>
    public TimeSpan BlockedLength
    {
        get { return VisitLength; }
    }

    /// <summary>Number of grid slots a booking holds, rounded up so a partly used slot still counts.</summary>
    public int SlotsPerBooking
    {
        get { return (int)Math.Ceiling(BlockedLength.TotalMinutes / SlotLength.TotalMinutes); }
    }

    /// <summary>When the visit ends, given when it starts.</summary>
    /// <param name="startUtc">Visit start (UTC).</param>
    public DateTime GetEndUtc(DateTime startUtc)
    {
        return startUtc.Add(VisitLength);
    }

    /// <summary>
    /// The start time of every slot a booking holds, e.g. 19:00 gives 19:00, 19:30, 20:00, 20:30.
    /// One ReservationSlot row is saved for each value.
    /// </summary>
    /// <param name="startUtc">Visit start (UTC), already on the slot grid.</param>
    public IReadOnlyList<DateTime> GetSlotStartsUtc(DateTime startUtc)
    {
        List<DateTime> slots = new List<DateTime>();
        for (int i = 0; i < SlotsPerBooking; i++)
        {
            slots.Add(startUtc.Add(SlotLength * i));
        }

        return slots;
    }

    /// <summary>True when a local start time is on the booking grid, e.g. 19:00 or 19:30 but not 19:15.</summary>
    /// <param name="localTime">A start time in Malaysia time.</param>
    public bool IsOnSlotGrid(TimeOnly localTime)
    {
        int minutesSinceMidnight = (localTime.Hour * 60) + localTime.Minute;
        return localTime.Second == 0 && minutesSinceMidnight % _options.SlotIntervalMinutes == 0;
    }

    /// <summary>
    /// True when two time ranges share at least one moment. Ranges that only touch
    /// (one ends at 21:00, the next starts at 21:00) do NOT overlap.
    /// </summary>
    /// <param name="firstStart">Start of the first range.</param>
    /// <param name="firstEnd">End of the first range.</param>
    /// <param name="secondStart">Start of the second range.</param>
    /// <param name="secondEnd">End of the second range.</param>
    public static bool Overlaps(DateTime firstStart, DateTime firstEnd, DateTime secondStart, DateTime secondEnd)
    {
        return firstStart < secondEnd && secondStart < firstEnd;
    }
}