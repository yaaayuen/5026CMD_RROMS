using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
/// <summary>All filtered order rows and totals computed from exactly those rows.</summary>
public class OrderMonitoringResult
{
    public IReadOnlyList<OrderMonitoringRow> Rows { get; set; } = Array.Empty<OrderMonitoringRow>();
    public int Count { get { return Rows.Count; } }
    public decimal OrderTotal { get { return Rows.Sum(r => r.TotalAmount); } }
}
