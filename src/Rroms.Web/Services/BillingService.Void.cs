using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;
public partial class BillingService
{
    /// <summary>Corrects the entire receipt atomically. This records a mistake; it does not issue a financial refund.</summary>
    public async Task<ServiceResult<PaymentRecordingResult>> VoidPaymentAsync(int paymentId, string? reason, string adminId)
    {
        bool admin = await (from u in _context.Users join ur in _context.UserRoles on u.Id equals ur.UserId
            join role in _context.Roles on ur.RoleId equals role.Id
            where u.Id == adminId && u.IsActive && role.Name == RoleNames.Administrator select u.Id).AnyAsync();
        if (!admin) return ServiceResult<PaymentRecordingResult>.Failure("Only an active administrator can void payments.");
        reason = reason?.Trim();
        if (reason == null || reason.Length < 5 || reason.Length > 200)
            return ServiceResult<PaymentRecordingResult>.Failure("Enter a void reason of 5 to 200 characters.");
        var payment = await _context.Payments.AsNoTracking().Include(p => p.Order).SingleOrDefaultAsync(p => p.Id == paymentId);
        if(payment == null || payment.Status != PaymentStatus.Paid)
            return ServiceResult<PaymentRecordingResult>.Failure("This payment is unavailable or has already been voided.");
        var group = await _context.Payments.Include(p => p.Order).Where(p => p.Status == PaymentStatus.Paid
            && p.Order!.ReservationId == payment.Order!.ReservationId && p.Order.CustomerId == payment.Order.CustomerId
            && (payment.ReceiptBatchId != null ? p.ReceiptBatchId == payment.ReceiptBatchId
                : p.ReceiptBatchId == null && p.PaidAtUtc == payment.PaidAtUtc && p.StaffId == payment.StaffId && p.Method == payment.Method))
            .ToListAsync();
        if(group.Count == 0) return ServiceResult<PaymentRecordingResult>.Failure("This receipt has already changed. Refresh the page.");
        var groupIds = group.Select(p => p.Id).ToList();
        if (await _context.Set<Rroms.Web.Models.PaymentRefund>().AnyAsync(r => groupIds.Contains(r.PaymentId)))
            return ServiceResult<PaymentRecordingResult>.Failure("A receipt with refund history cannot be voided.");
        DateTime now = DateTime.UtcNow;
        foreach(var row in group) { row.Status = PaymentStatus.Voided; row.VoidedAtUtc = now; row.VoidedById = adminId; row.VoidReason = reason; }
        try { await _context.SaveChangesAsync(); }
        catch(DbUpdateConcurrencyException) {
            foreach(var row in group) _context.Entry(row).State = EntityState.Detached;
            return ServiceResult<PaymentRecordingResult>.Failure("This receipt has already changed. Refresh the page.");
        }
        bool notified = true;
        try {
            await _notifications.NotifyUserAsync(payment.Order!.CustomerId, NotificationType.General, "Payment voided",
                $"Payment for order #{string.Join(", #", group.Select(p => p.OrderId))} was voided. Please contact the counter about the corrected payment.",
                $"/Customer/Receipts/Details/{payment.Id}");
        } catch(Exception ex) {
            notified = false;
            _logger.LogError(ex, "Receipt {PaymentId} was voided but notification failed.", paymentId);
        }
        return ServiceResult<PaymentRecordingResult>.Success(new() { PaymentId = paymentId, NotificationSent = notified });
    }
}
