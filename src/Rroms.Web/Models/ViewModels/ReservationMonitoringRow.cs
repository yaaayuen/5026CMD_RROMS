using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
/// <summary>Read-only reservation row for administrator monitoring.</summary>
public class ReservationMonitoringRow
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public int PartySize { get; set; }
    public string TableLabel { get; set; } = string.Empty;
    public string AreaName { get; set; } = string.Empty;
    public ReservationStatus Status { get; set; }
}
