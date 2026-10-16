using Rroms.Web.Contracts;

namespace Rroms.Web.Models.ViewModels;

/// <summary>The saved order total and original order lines needed to display a bill.</summary>
public class BillSummary
{
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string TableLabel { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime PlacedAtUtc { get; set; }
    public int? PaidPaymentId { get; set; }
    public List<BillLine> Lines { get; set; } = new List<BillLine>();
}
