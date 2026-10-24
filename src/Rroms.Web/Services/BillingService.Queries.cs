using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;
public partial class BillingService
{
    public async Task<BillingDashboard> GetDashboardAsync(string view = "unpaid", string? search = null, int page = 1, DateTime? nowUtc = null)
    {
        DateTime start = MalaysiaTime.ToUtc(MalaysiaTime.FromUtc(nowUtc ?? DateTime.UtcNow).Date);
        DateTime end = start.AddDays(1);
        var completed = _context.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.Completed);
        var paid = _context.Payments.AsNoTracking().Where(p => p.Status == PaymentStatus.Paid);
        var unpaid = completed.Where(o => !paid.Any(p => p.OrderId == o.Id));
        // Materialise only the values needed for decimal sums (also supported by SQLite tests).
        var outstanding = await unpaid.Select(o => o.TotalAmount).ToListAsync();
        var today = await paid.Where(p => p.Method != PaymentMethod.SimulatedOnline && p.PaidAtUtc >= start && p.PaidAtUtc < end)
            .Select(p => new { p.Amount, p.Method }).ToListAsync();
        var todayOrders = completed.Where(o => paid.Any(p => p.OrderId == o.Id && p.Method != PaymentMethod.SimulatedOnline && p.PaidAtUtc >= start && p.PaidAtUtc < end));
        var result = new BillingDashboard { UnpaidCount = outstanding.Count, Outstanding = outstanding.Sum(),
            PaidToday = today.Sum(p => p.Amount), PaymentCount = today.Count,
            Methods = today.GroupBy(p => p.Method).ToDictionary(g => g.Key, g => g.Count()),
            AllCount = await completed.CountAsync(), PaidTodayCount = await todayOrders.CountAsync() };
        var query = view == "all" ? completed : view == "paid" ? todayOrders : unpaid;
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            int.TryParse(term.TrimStart('#'), out int id);
            query = query.Where(o => o.Id == id || o.Customer!.FullName.Contains(term) || o.Reservation!.DiningTable!.Label.Contains(term));
        }
        result.Total = await query.CountAsync();
        result.Page = Math.Clamp(page, 1, Math.Max(1, (result.Total + 19) / 20));
        result.Rows = await query.OrderByDescending(o => o.PlacedAtUtc).ThenByDescending(o => o.Id)
            .Skip((result.Page - 1) * 20).Take(20).Select(o => new BillSummary {
                IsRefunded = _context.Set<Rroms.Web.Models.PaymentRefund>().Any(r => r.Payment!.OrderId == o.Id && r.Status == RefundStatus.Completed),
                OrderId = o.Id, ReservationId = o.ReservationId, CustomerName = o.Customer!.FullName,
                TableLabel = o.Reservation!.DiningTable!.Label, AreaName = o.Reservation.DiningTable.DiningArea!.Name,
                ItemCount = o.Items.Sum(i => i.Quantity), TotalAmount = o.TotalAmount, Status = o.Status, PlacedAtUtc = o.PlacedAtUtc,
                PaidPaymentId = paid.Where(p => p.OrderId == o.Id).Select(p => (int?)p.Id).FirstOrDefault(),
                PaidMethod = paid.Where(p => p.OrderId == o.Id).Select(p => (PaymentMethod?)p.Method).FirstOrDefault(),
                PaidAtUtc = paid.Where(p => p.OrderId == o.Id).Select(p => (DateTime?)p.PaidAtUtc).FirstOrDefault()
            }).ToListAsync();
        return result;
    }
    public async Task<IReadOnlyList<CustomerReceiptLink>> GetCustomerReceiptLinksAsync(string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId)) return Array.Empty<CustomerReceiptLink>();
        var rows = await _context.Payments.AsNoTracking().Where(p => p.Status == PaymentStatus.Paid
            && p.Order!.CustomerId == customerId && p.Order.Reservation!.CustomerId == customerId)
            .Select(p => new { p.Id, p.ReceiptBatchId, p.Order!.ReservationId, p.PaidAtUtc, p.StaffId, p.Method }).ToListAsync();
        return rows.GroupBy(p => new { p.ReservationId, p.ReceiptBatchId, p.PaidAtUtc, p.StaffId, p.Method })
            .Select(g => new CustomerReceiptLink(g.Key.ReservationId, g.Min(p => p.Id))).ToList();
    }
}
