namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// Table occupancy and reservation analytics for a date range (WL-4).
/// Built by IAnalyticsService and shown on the admin Occupancy page; the WL-5 dashboard draws the same numbers as charts.
/// </summary>
/// <remarks>
/// "Occupied" means the table was really used: the booking is Seated or Completed.
/// Booked time is counted from ReservationSlot rows (30 minutes each), so early departures,
/// cancellations and no-shows are already reflected, because those slots were deleted (WL-3).
/// </remarks>
public class OccupancyReport
{
    /// <summary>First day of the range (Malaysia date, inclusive).</summary>
    public DateOnly From { get; set; }

    /// <summary>Last day of the range (Malaysia date, inclusive).</summary>
    public DateOnly To { get; set; }

    /// <summary>Table-hours really used: Seated or Completed slots × 0.5 hour.</summary>
    public double OccupiedTableHours { get; set; }

    /// <summary>Table-hours the restaurant could offer: for each open day, opening hours × active tables.</summary>
    public double AvailableTableHours { get; set; }

    /// <summary>
    /// Table utilisation % = OccupiedTableHours ÷ AvailableTableHours × 100, rounded to 1 decimal place.
    /// 0 when nothing was available (e.g. every day closed).
    /// </summary>
    public double UtilisationPercent { get; set; }

    /// <summary>Peak hours: occupied table-hours for each weekday × hour of the day.</summary>
    public OccupancyHeatmap Heatmap { get; set; } = new OccupancyHeatmap();

    /// <summary>Reservation trend: one row per day in the range, oldest first.</summary>
    public IReadOnlyList<DailyReservationCount> DailyTrend { get; set; } = new List<DailyReservationCount>();

    /// <summary>
    /// Confirmed bookings still to come in the next <see cref="UpcomingDays"/> days.
    /// Shown separately so managers can see how busy it will be, without mixing future plans into utilisation.
    /// </summary>
    public int UpcomingConfirmedBookings { get; set; }

    /// <summary>Guests in those upcoming Confirmed bookings.</summary>
    public int UpcomingConfirmedGuests { get; set; }

    /// <summary>How many days ahead the "upcoming" figures look.</summary>
    public int UpcomingDays { get; set; }
}
