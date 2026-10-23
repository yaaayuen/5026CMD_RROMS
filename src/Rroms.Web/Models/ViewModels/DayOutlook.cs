namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// One upcoming day in the analytics outlook (WL-4): what is booked so far, and what is usual for that weekday,
/// worked out from the same weekday over the last few weeks. A simple average, not machine learning.
/// </summary>
public class DayOutlook
{
    /// <summary>The day (Malaysia).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Pending, Confirmed or Seated bookings for that day so far.</summary>
    public int BookedSoFar { get; set; }

    /// <summary>Guests in those bookings.</summary>
    public int GuestsBookedSoFar { get; set; }

    /// <summary>Average visits that really happened on this weekday (Seated or Completed) over the last weeks.</summary>
    public double UsualBookings { get; set; }

    /// <summary>Average guests who really dined on this weekday over the last weeks.</summary>
    public double UsualGuests { get; set; }

    /// <summary>The hour (0-23) with the most occupied table time on this weekday, or null without history.</summary>
    public int? UsualBusiestHour { get; set; }

    /// <summary>"Busy", "Normal", "Quiet", or "No history" when there is no past data at all.</summary>
    public string Level { get; set; } = string.Empty;
}
