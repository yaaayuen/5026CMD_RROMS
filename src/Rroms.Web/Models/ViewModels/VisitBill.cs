using Rroms.Web.Contracts;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// One bill for a whole visit: every order of a reservation that still needs paying, so the customer
/// pays once and gets one receipt even when they ordered several times (pre-order + more at the table).
/// </summary>
public class VisitBill
{
    public int ReservationId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string TableLabel { get; set; } = string.Empty;

    /// <summary>Orders of the visit that are neither cancelled nor paid, oldest first.</summary>
    public List<BillSummary> UnpaidOrders { get; set; } = new List<BillSummary>();

    /// <summary>The amount to collect: the saved totals of all unpaid orders.</summary>
    public decimal Total => UnpaidOrders.Sum(order => order.TotalAmount);

    /// <summary>The kitchen has finished every unpaid order, so the visit's bill can be paid.</summary>
    public bool ReadyToPay => UnpaidOrders.Count > 0 && UnpaidOrders.All(order => order.Status == OrderStatus.Completed);
}
