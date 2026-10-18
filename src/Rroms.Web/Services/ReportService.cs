using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;
/// <summary>DA-3 read-only reports using saved order prices and recorded Paid payments.</summary>
public class ReportService : IReportService
{
    private readonly ApplicationDbContext _context;
    /// <summary>Receives the scoped database; no teammate service or status rules are changed.</summary>
    public ReportService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ReportDashboard>> GetReportAsync(ReportFilter filter)
    {
        ServiceResult<ReportRange> resolved = ReportDateRanges.Resolve(filter);
        if (!resolved.Succeeded)
        {
            return ServiceResult<ReportDashboard>.Failure(resolved.Errors.ToArray());
        }
        ReportRange range = resolved.Value!;
        DateTime fromUtc = MalaysiaTime.ToUtc(range.From.ToDateTime(TimeOnly.MinValue));
        DateTime untilUtc = MalaysiaTime.ToUtc(range.To.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var reservations = await _context.Reservations.AsNoTracking()
            .Where(r => r.StartUtc >= fromUtc && r.StartUtc < untilUtc)
            .Select(r => new { r.CustomerId, Name = r.Customer!.FullName, r.Status, r.PartySize }).ToListAsync();
        var orders = await _context.Orders.AsNoTracking()
            .Where(o => o.PlacedAtUtc >= fromUtc && o.PlacedAtUtc < untilUtc)
            .Select(o => new { o.CustomerId, Name = o.Customer!.FullName }).ToListAsync();
        IQueryable<Payment> paidQuery = _context.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid && p.PaidAtUtc >= fromUtc && p.PaidAtUtc < untilUtc);
        var payments = await paidQuery.Select(p => new
        {
            p.OrderId, p.Amount, p.Method, p.PaidAtUtc, p.Order!.CustomerId, Name = p.Order.Customer!.FullName
        }).ToListAsync();

        // Use the already selected Paid order IDs for item rows so the payment inclusion set is identical.
        List<int> paidOrderIds = payments.Select(p => p.OrderId).Distinct().ToList();
        HashSet<int> demoOrderIds = payments.Where(p => p.Method == PaymentMethod.SimulatedOnline)
            .Select(p => p.OrderId).ToHashSet();
        var lines = await _context.OrderItems.AsNoTracking().Where(i => paidOrderIds.Contains(i.OrderId))
            .Select(i => new { i.OrderId, i.MenuItemId, Name = i.MenuItem!.Name, i.Quantity, i.UnitPrice }).ToListAsync();

        var report = new ReportDashboard { Range = range };
        var statusRows = new List<ReservationStatusReportRow>();
        foreach (ReservationStatus status in Enum.GetValues<ReservationStatus>())
        {
            var matching = reservations.Where(r => r.Status == status).ToList();
            statusRows.Add(new ReservationStatusReportRow
            {
                Status = status, Count = matching.Count, Guests = matching.Sum(r => r.PartySize)
            });
        }
        report.Reservations = statusRows;
        var methodRows = new List<SalesMethodReportRow>();
        foreach (PaymentMethod method in Enum.GetValues<PaymentMethod>())
        {
            var matching = payments.Where(p => p.Method == method).ToList();
            methodRows.Add(new SalesMethodReportRow
            {
                Method = method, Count = matching.Count, Amount = matching.Sum(p => p.Amount)
            });
        }
        report.SalesMethods = methodRows;
        // Group in memory after the bounded projection: decimal SUM and local date conversion work on both providers.
        var byDate = payments.GroupBy(p => DateOnly.FromDateTime(MalaysiaTime.FromUtc(p.PaidAtUtc)))
            .ToDictionary(g => g.Key, g => new DailySalesReportRow
            {
                Date = g.Key, Count = g.Count(), PaidAmount = g.Sum(p => p.Amount),
                DemoAmount = g.Where(p => p.Method == PaymentMethod.SimulatedOnline).Sum(p => p.Amount)
            });
        var dailyRows = new List<DailySalesReportRow>();
        for (DateOnly day = range.From; day <= range.To; day = day.AddDays(1))
        {
            dailyRows.Add(byDate.TryGetValue(day, out DailySalesReportRow? row) ? row : new DailySalesReportRow { Date = day });
        }
        report.DailySales = dailyRows;
        var popularRows = lines.GroupBy(i => i.MenuItemId).Select(g => new PopularItemReportRow
        {
            MenuItemId = g.Key, Name = g.First().Name, Quantity = g.Sum(i => i.Quantity),
            LineValue = g.Sum(i => i.Quantity * i.UnitPrice),
            DemoQuantity = g.Where(i => demoOrderIds.Contains(i.OrderId)).Sum(i => i.Quantity),
            DemoLineValue = g.Where(i => demoOrderIds.Contains(i.OrderId)).Sum(i => i.Quantity * i.UnitPrice)
        }).OrderByDescending(i => i.Quantity).ThenBy(i => i.MenuItemId).ToList();
        for (int index = 0; index < popularRows.Count; index++)
        {
            popularRows[index].Rank = index + 1;
        }
        report.PopularItems = popularRows;

        // Aggregate each source separately. Joining reservations, orders and payments would multiply customer totals.
        var customers = new Dictionary<string, CustomerActivityReportRow>();
        foreach (var reservation in reservations)
        {
            GetCustomer(customers, reservation.CustomerId, reservation.Name).ReservationCount++;
        }
        foreach (var order in orders)
        {
            GetCustomer(customers, order.CustomerId, order.Name).OrderCount++;
        }
        foreach (var payment in payments)
        {
            CustomerActivityReportRow row = GetCustomer(customers, payment.CustomerId, payment.Name);
            row.PaidCount++;
            row.PaidAmount += payment.Amount;
            if (payment.Method == PaymentMethod.SimulatedOnline)
            {
                row.DemoAmount += payment.Amount;
            }
        }
        report.Customers = customers.Values.OrderByDescending(c => c.PaidAmount)
            .ThenBy(c => c.CustomerName).ThenBy(c => c.CustomerId).ToList();
        return ServiceResult<ReportDashboard>.Success(report);
    }

    private static CustomerActivityReportRow GetCustomer(Dictionary<string, CustomerActivityReportRow> rows, string id, string name)
    {
        if (!rows.TryGetValue(id, out CustomerActivityReportRow? row))
        {
            row = new CustomerActivityReportRow { CustomerId = id, CustomerName = name };
            rows.Add(id, row);
        }
        return row;
    }
}
