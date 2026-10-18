using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
/// <summary>Independent reservation, order and Paid activity for a customer ID, avoiding multiplied joins.</summary>
public class CustomerActivityReportRow
{
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public int ReservationCount { get; set; }
    public int OrderCount { get; set; }
    public int PaidCount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal DemoAmount { get; set; }
    public decimal NonDemoAmount { get { return PaidAmount - DemoAmount; } }
}
