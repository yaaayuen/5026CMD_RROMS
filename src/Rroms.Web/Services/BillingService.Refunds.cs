using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;
public partial class BillingService
{
    public const string NoRefundLinesMessage = "Choose at least one dish and how many to refund.";
    public const string InvalidRefundLineMessage = "A chosen dish is not on this receipt, or its refund quantity is more than was ordered.";

    public Task<ServiceResult<PaymentRecordingResult>> RequestRefundAsync(int paymentId, string? reason, string customerId) =>
        ChangeRefundAsync(paymentId, reason, customerId, RefundStatus.Requested, false, null);
    public Task<ServiceResult<PaymentRecordingResult>> RecordRefundAsync(int paymentId, string? reason, string staffId, bool moneyReturned,
        IReadOnlyCollection<RefundLineInput>? lines = null) =>
        ChangeRefundAsync(paymentId, reason, staffId, RefundStatus.Completed, moneyReturned, lines);
    public Task<ServiceResult<PaymentRecordingResult>> RejectRefundAsync(int paymentId, string? reason, string staffId) =>
        ChangeRefundAsync(paymentId, reason, staffId, RefundStatus.Rejected, false, null);

    private async Task<ServiceResult<PaymentRecordingResult>> ChangeRefundAsync(int paymentId, string? reason, string actorId, RefundStatus target, bool returned,
        IReadOnlyCollection<RefundLineInput>? lines)
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
            return ServiceResult<PaymentRecordingResult>.Failure("Confirm that the refund amount has been returned.");
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
        // Dish refund: work out each payment's share before anything is saved.
        Dictionary<int, (decimal Amount, List<PaymentRefundLine> Lines)>? chosen = null;
        if(target == RefundStatus.Completed && lines != null) {
            var picked = await RefundLinesAsync(group, lines);
            if(!picked.Succeeded) return ServiceResult<PaymentRecordingResult>.Failure(picked.Errors.ToArray());
            chosen = picked.Value;
        }
        DateTime now = DateTime.UtcNow;
        if(rows.Count == 0) {
            Guid batch = Guid.NewGuid();
            rows = group.Select(p => new PaymentRefund { PaymentId = p.Id, Payment = p, BatchId = batch,
                Amount = p.Amount, Reason = reason, RequestedById = actorId, RequestedAtUtc = now }).ToList();
            _context.Set<PaymentRefund>().AddRange(rows);
        }
        if(chosen != null) {
            foreach(var row in rows) {
                (decimal Amount, List<PaymentRefundLine> Lines) share = chosen.TryGetValue(row.PaymentId, out var found) ? found : (0m, new List<PaymentRefundLine>());
                row.Amount = share.Amount;
                row.Lines.AddRange(share.Lines);
            }
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
            string what = target == RefundStatus.Completed
                ? (chosen != null ? "Refund of " : "Full refund of ") + Money.Format(rows.Sum(r => r.Amount))
                : "Refund request";
            await _notifications.NotifyUserAsync(payment.Order!.CustomerId, NotificationType.General, "Refund " + state,
                $"{what} for receipt R-{group[0].Id:D4} {state}.",
                $"/Customer/Receipts/Details/{group[0].Id}");
        } catch(Exception ex) {
            notified = false;
            _logger.LogError(ex, "Refund for payment {PaymentId} saved but notification failed.", paymentId);
        }
        return ServiceResult<PaymentRecordingResult>.Success(new() { PaymentId = group[0].Id, NotificationSent = notified });
    }

    /// <summary>
    /// Turns the chosen dishes into refund amounts per payment. Each dish gets its share of that order's paid total,
    /// so service charge and tax are refunded with it; a fully refunded order gets back exactly what was paid.
    /// </summary>
    private async Task<ServiceResult<Dictionary<int, (decimal Amount, List<PaymentRefundLine> Lines)>>> RefundLinesAsync(
        List<Payment> group, IReadOnlyCollection<RefundLineInput> lines)
    {
        var wanted = lines.Where(l => l.Quantity != 0).ToList();
        if(wanted.Count == 0)
            return ServiceResult<Dictionary<int, (decimal, List<PaymentRefundLine>)>>.Failure(NoRefundLinesMessage);
        var orderIds = group.Select(p => p.OrderId).ToList();
        var items = await _context.OrderItems.AsNoTracking().Include(i => i.MenuItem)
            .Where(i => orderIds.Contains(i.OrderId)).ToListAsync();
        if(wanted.Any(l => l.Quantity < 0) || wanted.GroupBy(l => l.OrderItemId).Any(g => g.Count() > 1)
            || wanted.Any(l => items.SingleOrDefault(i => i.Id == l.OrderItemId) is not { } item || l.Quantity > item.Quantity))
            return ServiceResult<Dictionary<int, (decimal, List<PaymentRefundLine>)>>.Failure(InvalidRefundLineMessage);

        var result = new Dictionary<int, (decimal Amount, List<PaymentRefundLine> Lines)>();
        foreach(Payment p in group) {
            var orderItems = items.Where(i => i.OrderId == p.OrderId).ToList();
            decimal itemsTotal = orderItems.Sum(i => i.UnitPrice * i.Quantity);
            var mine = wanted.Where(l => orderItems.Any(i => i.Id == l.OrderItemId)).ToList();
            var refundLines = mine.Select(l => {
                OrderItem item = orderItems.Single(i => i.Id == l.OrderItemId);
                decimal share = itemsTotal <= 0 ? 0m
                    : decimal.Round(item.UnitPrice * l.Quantity * p.Amount / itemsTotal, 2, MidpointRounding.AwayFromZero);
                return new PaymentRefundLine { OrderItemId = item.Id, DishName = item.MenuItem?.Name ?? "Dish",
                    Quantity = l.Quantity, Amount = share };
            }).ToList();
            bool wholeOrder = orderItems.Count > 0 && orderItems.All(i => mine.Any(l => l.OrderItemId == i.Id && l.Quantity == i.Quantity));
            decimal amount = wholeOrder ? p.Amount : Math.Min(p.Amount, refundLines.Sum(l => l.Amount));
            if(wholeOrder && refundLines.Count > 0) {
                // Put any rounding cent on the last line so the lines add up to exactly what was paid.
                refundLines[^1].Amount += p.Amount - refundLines.Sum(l => l.Amount);
            }
            result[p.Id] = (amount, refundLines);
        }
        if(result.Values.Sum(v => v.Amount) <= 0)
            return ServiceResult<Dictionary<int, (decimal, List<PaymentRefundLine>)>>.Failure(NoRefundLinesMessage);
        return ServiceResult<Dictionary<int, (decimal, List<PaymentRefundLine>)>>.Success(result);
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
        var rows = await query.Where(r => keys.Contains(r.BatchId)).Include(r => r.Lines).Include(r => r.Payment!).ThenInclude(p => p.Order!).ThenInclude(o => o.Customer).ToListAsync();
        return new RefundList { Total = count, Page = current,
            Rows = rows.GroupBy(r => r.BatchId).Select(g => MapRefund(g.ToList())).OrderByDescending(r => r.RequestedAtUtc).ThenBy(r => r.BatchId).ToList() };
    }
    private static RefundSummary MapRefund(List<PaymentRefund> rows)
    {
        var first = rows.OrderBy(r => r.PaymentId).First();
        return new RefundSummary { BatchId = first.BatchId, PaymentId = first.PaymentId, Amount = rows.Sum(r => r.Amount),
            CustomerName = first.Payment!.Order!.Customer!.FullName, Method = first.Payment.Method, Status = first.Status,
            Reason = first.Reason, RequestedAtUtc = first.RequestedAtUtc, ProcessedAtUtc = first.ProcessedAtUtc,
            ProcessedById = first.ProcessedById, DecisionReason = first.DecisionReason,
            Lines = rows.SelectMany(r => r.Lines).OrderBy(l => l.OrderItemId)
                .Select(l => new RefundLineSummary { DishName = l.DishName, Quantity = l.Quantity, Amount = l.Amount }).ToList() };
    }
}
