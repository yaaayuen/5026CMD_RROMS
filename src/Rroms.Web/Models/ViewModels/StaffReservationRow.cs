namespace Rroms.Web.Models.ViewModels;
/// <summary>WL's booking details with the customer's current profile preferences.</summary>
public class StaffReservationRow
{
    public ReservationDetails Reservation { get; set; } = new ReservationDetails();
    public string? DiningPreferences { get; set; }
}
