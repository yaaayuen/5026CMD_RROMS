namespace Rroms.Web.Models;

/// <summary>
/// The normal opening hours for one day of the week (WL-1). There is one row per day.
/// </summary>
/// <remarks>
/// Times are Malaysia local wall-clock times, not UTC. "Opens at 11:00 every Monday" is a
/// repeating rule, not a moment in time, so the shared "store timestamps in UTC" rule does not apply.
/// Opening past midnight is not supported: <see cref="ClosesAt"/> must be later than <see cref="OpensAt"/>.
/// </remarks>
public class OperatingHour
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>The day these hours apply to. Each day appears once (unique index).</summary>
    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>Opening time, Malaysia local time.</summary>
    public TimeOnly OpensAt { get; set; }

    /// <summary>Closing time, Malaysia local time. Must be later than <see cref="OpensAt"/>.</summary>
    public TimeOnly ClosesAt { get; set; }

    /// <summary>True when the restaurant does not open at all on this day. The times are then ignored.</summary>
    public bool IsClosed { get; set; }
}
