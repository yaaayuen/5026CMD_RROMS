using Rroms.Web.Contracts;

namespace Rroms.Web.Models.ViewModels;

/// <summary>Receipt data; the amount comes from the saved payment, not today's menu.</summary>
public class ReceiptSummary
{
    public int PaymentId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public DateTime PaidAtUtc { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public BillSummary Bill { get; set; } = new BillSummary();
    /// <summary>
    /// Every order paid together with this payment (one entry for a single-order bill, several for a
    /// whole-visit bill). The receipt lists them all.
    /// </summary>
    public List<BillSummary> Bills { get; set; } = new List<BillSummary>();
    /// <summary>The sum of all payments on this receipt.</summary>
    public decimal TotalPaid { get; set; }
}
