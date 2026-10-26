using Rroms.Web.Contracts;

namespace Rroms.Web.Common;

/// <summary>
/// How a reservation status looks on screen (WL-3), shared by the customer and staff pages so the colours always match.
/// </summary>
public static class ReservationStatusDisplay
{
    /// <summary>Shared status pill classes (tg-status in site.css), e.g. Pending is amber and Confirmed is teal.</summary>
    /// <param name="status">The booking's status.</param>
    public static string BadgeClass(ReservationStatus status)
    {
        return "tg-status " + status switch
        {
            ReservationStatus.Pending => "is-pending",
            ReservationStatus.Confirmed => "is-confirmed",
            ReservationStatus.Seated => "is-seated",
            ReservationStatus.Completed => "is-done",
            ReservationStatus.NoShow => "is-noshow",
            _ => "is-cancelled"
        };
    }

    /// <summary>The button text staff see for moving a booking to a status.</summary>
    /// <param name="to">The status the button moves the booking to.</param>
    public static string ActionLabel(ReservationStatus to)
    {
        return to switch
        {
            ReservationStatus.Confirmed => "Confirm",
            ReservationStatus.Seated => "Seat",
            ReservationStatus.Completed => "Complete",
            ReservationStatus.Cancelled => "Cancel",
            ReservationStatus.NoShow => "Mark absent",
            _ => to.ToString()
        };
    }

    /// <summary>
    /// The status name shown on badges. NoShow is shown as "Absent"; the stored value stays NoShow,
    /// so the database and the shared enum do not change.
    /// </summary>
    /// <param name="status">The booking's status.</param>
    public static string Label(ReservationStatus status)
    {
        return status == ReservationStatus.NoShow ? "Absent" : status.ToString();
    }
}
