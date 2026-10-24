using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
public class BillingDashboard
{
    public int UnpaidCount { get; set; }
    public decimal Outstanding { get; set; }
    public decimal PaidToday { get; set; }
    public int PaymentCount { get; set; }
    public Dictionary<PaymentMethod, int> Methods { get; set; } = new();
    public int AllCount { get; set; }
    public int PaidTodayCount { get; set; }
    public int Total { get; set; }
    public int Page { get; set; }
    public List<BillSummary> Rows { get; set; } = new();
}
public record CustomerReceiptLink(int ReservationId, int PaymentId);
public record ReservationReceiptList(IReadOnlyList<ReservationSummary> Reservations, ILookup<int, CustomerReceiptLink> Receipts);
