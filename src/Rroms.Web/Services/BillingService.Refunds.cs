using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;
public partial class BillingService
{
    public Task<ServiceResult<PaymentRecordingResult>> RequestRefundAsync(int paymentId, string? reason, string customerId) =>
        ChangeRefundAsync(paymentId, reason, customerId, RefundStatus.Requested, false);
    public Task<ServiceResult<PaymentRecordingResult>> RecordRefundAsync(int paymentId, string? reason, string staffId, bool moneyReturned) =>
        ChangeRefundAsync(paymentId, reason, staffId, RefundStatus.Completed, moneyReturned);
    public Task<ServiceResult<PaymentRecordingResult>> RejectRefundAsync(int paymentId, string? reason, string staffId) =>
        ChangeRefundAsync(paymentId, reason, staffId, RefundStatus.Rejected, false);

    private async Task<ServiceResult<PaymentRecordingResult>> ChangeRefundAsync(int paymentId, string? reason, string actorId, RefundStatus target, bool returned)
    {
        bool customer = target == RefundStatus.Requested;
        if(customer) {
            if(!await _context.Users.AnyAsync(u => u.Id == actorId && u.IsActive))
                return ServiceResult<PaymentRecordingResult>.Failure("An active customer account is required.");
        } else if(!await IsActiveStaffAsync(actorId))
            return ServiceResult<PaymentRecordingResult>.Failure(InvalidStaffMessage);
        reason = reason?.Trim();
        if(reason == null || reason.Length < 5 || reason.Length > 200)
            return ServiceResult<PaymentRecordingResult>.Failure("Enter a reason of 5 to 200 characters.");
        var payment = await _context.Payments.AsNoTracking().Include(p => p.Order)
            .SingleOrDefaultAsync(p => p.Id == paymentId && (!customer || p.Order!.CustomerId == actorId));
        if(payment == null || payment.Status != PaymentStatus.Paid || !PaymentMethods.IsSupported(payment.Method))
            return ServiceResult<PaymentRecordingResult>.Failure("Only an available paid receipt can be refunded.");
        if(target == RefundStatus.Completed && !returned)
            return ServiceResult<PaymentRecordingResult>.Failure("Confirm that the full amount has been returned.");
        var group = await _context.Payments.Include(p => p.Order!).ThenInclude(o => o.Customer)
            .Where(p => p.Status == PaymentStatus.Paid && p.Order!.CustomerId == payment.Order!.CustomerId
                && p.Order.ReservationId == payment.Order.ReservationId
                && (payment.ReceiptBatchId != null ? p.ReceiptBatchId == payment.ReceiptBatchId
                    : p.ReceiptBatchId == null && p.PaidAtUtc == payment.PaidAtUtc && p.StaffId == payment.StaffId && p.Method == payment.Method))
            .OrderBy(p => p.Id).ToListAsync();
        var ids = group.Select(p => p.Id).ToList();
        var rows = await _context.Set<PaymentRefund>().Where(r => ids.Contains(r.PaymentId)).ToListAsync();
        if(group.Count == 0 || group.Sum(p => p.Amount) <= 0)
            return ServiceResult<PaymentRecordingResult>.Failure("There is no positive paid amount to refund.");
        if(rows.Count > 0 && (customer || rows.Count != group.Count || rows.Any(r => r.Status != RefundStatus.Requested)))
            return ServiceResult<PaymentRecordingResult>.Failure("This receipt already has a refund decision or request. Refresh to see its status.");
        if(rows.Count == 0 && target == RefundStatus.Rejected)
            return ServiceResult<PaymentRecordingResult>.Failure("There is no pending request to reject.");
        DateTime now = DateTime.UtcNow;
        if(rows.Count == 0) {
            Guid batch = Guid.NewGuid();
            rows = group.Select(p => new PaymentRefund { PaymentId = p.Id, Payment = p, BatchId = batch,
                Amount = p.Amount, Reason = reason, RequestedById = actorId, RequestedAtUtc = now }).ToList();
            _context.Set<PaymentRefund>().AddRange(rows);
        }
        foreach(var row in rows) {
            row.Status = target;
            if(!customer) { row.ProcessedById = actorId; row.ProcessedAtUtc = now; row.DecisionReason = reason; }
        }
        // Updating each payment's concurrency token serialises refund/void and duplicate requests.
        // One SaveChanges atomically saves every row of the receipt. Paid remains Paid: no re-charging.
        foreach(var p in group) p.RefundRevision++;
        try { await _context.SaveChangesAsync(); }
        catch(DbUpdateException ex) when (ex is DbUpdateConcurrencyException
            || ex.InnerException?.Message.Contains("UNIQUE constraint failed: PaymentRefunds.PaymentId", StringComparison.Ordinal) == true
            || ex.InnerException is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 2601 || sql.Number == 2627)
               && sql.Message.Contains("IX_PaymentRefunds_PaymentId", StringComparison.Ordinal)) {
            foreach(var row in rows) _context.Entry(row).State = EntityState.Detached;
            foreach(var p in group) _context.Entry(p).State = EntityState.Detached;
            return ServiceResult<PaymentRecordingResult>.Failure("This receipt changed in another request. Refresh before continuing.");
        }
        bool notified = true;
        try {
            string state = target == RefundStatus.Completed ? "recorded" : target == RefundStatus.Rejected ? "rejected" : "requested";
            await _notifications.NotifyUserAsync(payment.Order!.CustomerId, NotificationType.General, "Refund " + state,
                $"Full refund of {Money.Format(group.Sum(p => p.Amount))} for receipt R-{group[0].Id:D4} {state}.",
                $"/Customer/Receipts/Details/{group[0].Id}");
        } catch(Exception ex) {
            notified = false;
            _logger.LogError(ex, "Refund for payment {PaymentId} saved but notification failed.", paymentId);
        }
        return ServiceResult<PaymentRecordingResult>.Success(new() { PaymentId = group[0].Id, NotificationSent = notified });
    }

    public async Task<RefundList> GetRefundsAsync(string? customerId = null, int page = 1, RefundStatus? status = null)
    {
        var query = _context.Set<PaymentRefund>().AsNoTracking().AsQueryable();
        if(customerId != null) query = query.Where(r => r.Payment!.Order!.CustomerId == customerId);
        if(status.HasValue) query = query.Where(r => r.Status == status.Value);
        var batches = query.GroupBy(r => r.BatchId).Select(g => new { BatchId = g.Key, Date = g.Max(r => r.RequestedAtUtc) });
        int count = await batches.CountAsync();
        int current = Math.Clamp(page, 1, Math.Max(1, (count + 19) / 20));
        var keys = await batches.OrderByDescending(g => g.Date).ThenBy(g => g.BatchId).Skip((current - 1) * 20).Take(20).Select(g => g.BatchId).ToListAsync();
        var rows = await query.Where(r => keys.Contains(r.BatchId)).Include(r => r.Payment!).ThenInclude(p => p.Order!).ThenInclude(o => o.Customer).ToListAsync();
        return new RefundList { Total = count, Page = current,
            Rows = rows.GroupBy(r => r.BatchId).Select(g => MapRefund(g.ToList())).OrderByDescending(r => r.RequestedAtUtc).ThenBy(r => r.BatchId).ToList() };
    }
    private static RefundSummary MapRefund(List<PaymentRefund> rows)
    {
        var first = rows.OrderBy(r => r.PaymentId).First();
        return new RefundSummary { BatchId = first.BatchId, PaymentId = first.PaymentId, Amount = rows.Sum(r => r.Amount),
            CustomerName = first.Payment!.Order!.Customer!.FullName, Method = first.Payment.Method, Status = first.Status,
            Reason = first.Reason, RequestedAtUtc = first.RequestedAtUtc, ProcessedAtUtc = first.ProcessedAtUtc,
            ProcessedById = first.ProcessedById, DecisionReason = first.DecisionReason };
    }
}
