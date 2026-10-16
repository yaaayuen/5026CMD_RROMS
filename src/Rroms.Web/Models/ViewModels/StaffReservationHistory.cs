namespace Rroms.Web.Models.ViewModels;
/// <summary>Past scheduled visits for the customer identified through a staff-visible booking.</summary>
public class StaffReservationHistory
{
    public int ReservationId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? DiningPreferences { get; set; }
    public IReadOnlyList<ReservationSummary> Reservations { get; set; } = Array.Empty<ReservationSummary>();
}
