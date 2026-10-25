using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>Billing, payment recording and ownership-filtered receipts. Owner: Darren (DA-1).</summary>
public interface IBillingService
{
    Task<ServiceResult<PaymentRecordingResult>> RequestRefundAsync(int paymentId, string? reason, string customerId);
    /// <summary>
    /// Records a refund. With no <paramref name="lines"/> the whole receipt is refunded; otherwise only the chosen
    /// dishes (each with its share of service charge and tax). A receipt can be refunded once.
    /// </summary>
    Task<ServiceResult<PaymentRecordingResult>> RecordRefundAsync(int paymentId, string? reason, string staffId, bool moneyReturned,
        IReadOnlyCollection<RefundLineInput>? lines = null);
    Task<ServiceResult<PaymentRecordingResult>> RejectRefundAsync(int paymentId, string? reason, string staffId);
    Task<RefundList> GetRefundsAsync(string? customerId = null, int page = 1, RefundStatus? status = null);
    Task<ServiceResult<PaymentRecordingResult>> VoidPaymentAsync(int paymentId, string? reason, string adminId);
    Task<BillingDashboard> GetDashboardAsync(string view = "unpaid", string? search = null, int page = 1, DateTime? nowUtc = null);
    Task<IReadOnlyList<CustomerReceiptLink>> GetCustomerReceiptLinksAsync(string customerId);
    /// <summary>Lists completed orders, optionally including orders that already have a Paid payment.</summary>
    Task<IReadOnlyList<BillSummary>> GetCompletedOrdersAsync(bool includePaid = false);
    /// <summary>Reads an order's bill for authorised staff; returns null for an unknown order.</summary>
    Task<BillSummary?> GetBillAsync(int orderId);
    /// <summary>Records the saved order total, using the staff ID obtained from the authenticated user.</summary>
    Task<ServiceResult<PaymentRecordingResult>> RecordPaymentAsync(int orderId, PaymentMethod method, string staffId, decimal? cashReceived = null, bool simulateDecline = false);
    /// <summary>The whole-visit bill: every unpaid, not-cancelled order of a reservation; null for an unknown reservation.</summary>
    Task<VisitBill?> GetVisitBillAsync(int reservationId);
    /// <summary>
    /// Pays every unpaid order of a visit in one step (one transaction) so the customer pays once and gets one receipt.
    /// Returns the first payment's ID, which opens the combined receipt.
    /// </summary>
    Task<ServiceResult<PaymentRecordingResult>> RecordVisitPaymentAsync(int reservationId, PaymentMethod method, string staffId, decimal? cashReceived = null, bool simulateDecline = false);
    /// <summary>Reads a paid or voided receipt for staff or administrators through their protected pages.</summary>
    Task<ReceiptSummary?> GetReceiptForStaffAsync(int paymentId);
    /// <summary>Reads a paid or voided receipt only when it belongs to the signed-in customer.</summary>
    Task<ReceiptSummary?> GetReceiptForCustomerAsync(int paymentId, string customerId);
}
