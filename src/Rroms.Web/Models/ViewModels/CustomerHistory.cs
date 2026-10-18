namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// Everything one customer sees on "My history": their own reservations, orders and payments, newest first.
/// Owner: YJ (YJ-5).
/// </summary>
public class CustomerHistory
{
    /// <summary>The customer's reservations, latest visit first.</summary>
    public IReadOnlyList<HistoryReservationRow> Reservations { get; set; } = new List<HistoryReservationRow>();

    /// <summary>The customer's orders, latest first. Cancelled orders are included with their status.</summary>
    public IReadOnlyList<HistoryOrderRow> Orders { get; set; } = new List<HistoryOrderRow>();

    /// <summary>Payments made for the customer's orders, latest first. Only payments with status Paid.</summary>
    public IReadOnlyList<HistoryPaymentRow> Payments { get; set; } = new List<HistoryPaymentRow>();
}
