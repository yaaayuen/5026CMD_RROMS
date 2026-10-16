using Rroms.Web.Contracts;

namespace Rroms.Web.Common;

/// <summary>
/// How a reservation status looks on screen (WL-3), shared by the customer and staff pages so the colours always match.
/// </summary>
public static class ReservationStatusDisplay
{
    /// <summary>Bootstrap badge classes for a status, e.g. Pending is yellow and Confirmed is green.</summary>
    /// <param name="status">The booking's status.</param>
    public static string BadgeClass(ReservationStatus status)
    {
        return status switch
        {
            ReservationStatus.Pending => "text-bg-warning",
            ReservationStatus.Confirmed => "text-bg-success",
            ReservationStatus.Seated => "text-bg-primary",
            ReservationStatus.Completed => "text-bg-secondary",
            _ => "text-bg-light border"
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
            ReservationStatus.NoShow => "No-show",
            _ => to.ToString()
        };
    }
}
