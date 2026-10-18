using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
/// <summary>Reservation count and guests for one current status.</summary>
public class ReservationStatusReportRow
{
    public ReservationStatus Status { get; set; }
    public int Count { get; set; }
    public int Guests { get; set; }
}
