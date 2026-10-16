using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>Billing, payment recording and ownership-filtered receipts. Owner: Darren (DA-1).</summary>
public interface IBillingService
{
    /// <summary>Lists completed orders, optionally including orders that already have a Paid payment.</summary>
    Task<IReadOnlyList<BillSummary>> GetCompletedOrdersAsync(bool includePaid = false);
    /// <summary>Reads an order's bill for authorised staff; returns null for an unknown order.</summary>
    Task<BillSummary?> GetBillAsync(int orderId);
    /// <summary>Records the saved order total, using the staff ID obtained from the authenticated user.</summary>
    Task<ServiceResult<PaymentRecordingResult>> RecordPaymentAsync(int orderId, PaymentMethod method, string staffId);
    /// <summary>Reads a Paid receipt for staff or administrators through their protected pages.</summary>
    Task<ReceiptSummary?> GetReceiptForStaffAsync(int paymentId);
    /// <summary>Reads a Paid receipt only when it belongs to the signed-in customer.</summary>
    Task<ReceiptSummary?> GetReceiptForCustomerAsync(int paymentId, string customerId);
}
