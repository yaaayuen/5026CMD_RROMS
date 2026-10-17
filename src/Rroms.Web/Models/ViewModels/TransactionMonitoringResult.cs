using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
/// <summary>All filtered transaction rows and totals computed from exactly those rows.</summary>
public class TransactionMonitoringResult
{
    public IReadOnlyList<TransactionMonitoringRow> Rows { get; set; } = Array.Empty<TransactionMonitoringRow>();
    public int Count { get { return Rows.Count; } }
    public decimal PaidTotal { get { return Rows.Where(r => r.Status == PaymentStatus.Paid).Sum(r => r.Amount); } }
    public decimal DemoPaidTotal { get { return Rows.Where(r => r.Status == PaymentStatus.Paid && r.Method == PaymentMethod.SimulatedOnline).Sum(r => r.Amount); } }
    public decimal NonDemoPaidTotal { get { return PaidTotal - DemoPaidTotal; } }
}
