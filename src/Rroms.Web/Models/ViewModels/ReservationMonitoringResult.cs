using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
/// <summary>All filtered reservation rows and totals computed from exactly those rows.</summary>
public class ReservationMonitoringResult
{
    public IReadOnlyList<ReservationMonitoringRow> Rows { get; set; } = Array.Empty<ReservationMonitoringRow>();
    public int Count { get { return Rows.Count; } }
    public int GuestTotal { get { return Rows.Sum(r => r.PartySize); } }
}
