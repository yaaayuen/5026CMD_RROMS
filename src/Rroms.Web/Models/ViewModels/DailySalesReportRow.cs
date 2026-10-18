using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
/// <summary>Paid totals grouped by Malaysia payment date, including zero days.</summary>
public class DailySalesReportRow
{
    public DateOnly Date { get; set; }
    public int Count { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal DemoAmount { get; set; }
    public decimal NonDemoAmount { get { return PaidAmount - DemoAmount; } }
}
