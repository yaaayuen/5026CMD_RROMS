using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Tests.Billing;
public partial class BillingServiceTests
{
    [Fact]
    public async Task RefundRequestThenCompletionPreservesWholeReceiptAndPreventsRecharge()
    {
        var (order, staff) = await SeedAsync(RoleNames.Administrator);
        await AddSecondOrderAsync(order, OrderStatus.Completed, 30m);
        var paid = await _service.RecordVisitPaymentAsync(order.ReservationId, PaymentMethod.Cash, staff.Id, 100m);
        int id = paid.Value!.PaymentId;
        Assert.False((await _service.RequestRefundAsync(id, "Wrong customer", staff.Id)).Succeeded);
        Assert.True((await _service.RequestRefundAsync(id, "Customer request", order.CustomerId)).Succeeded);
        Assert.False((await _service.RequestRefundAsync(id, "Duplicate request", order.CustomerId)).Succeeded);
        Assert.False((await _service.RecordRefundAsync(id, "Return confirmed", order.CustomerId, true)).Succeeded);
        Assert.False((await _service.RecordRefundAsync(id, "Return confirmed", staff.Id, false)).Succeeded);
        Assert.False((await _service.VoidPaymentAsync(id, "Void during refund", staff.Id)).Succeeded);
        Assert.True((await _service.RecordRefundAsync(id, "Return confirmed", staff.Id, true)).Succeeded);
        Assert.False((await _service.RecordRefundAsync(id, "Duplicate refund", staff.Id, true)).Succeeded);
        Assert.False((await _service.RecordVisitPaymentAsync(order.ReservationId, PaymentMethod.Cash, staff.Id)).Succeeded);
        Assert.False((await _service.VoidPaymentAsync(id, "Void after refund", staff.Id)).Succeeded);
        var receipt = await _service.GetReceiptForCustomerAsync(id, order.CustomerId);
        Assert.Equal(70m, receipt!.RefundedAmount);
        Assert.Equal(0m, receipt.NetPaid);
        Assert.Equal(RefundStatus.Completed, receipt.Refund!.Status);
        Assert.Equal("Customer request", receipt.Refund.Reason);
        Assert.Equal("Return confirmed", receipt.Refund.DecisionReason);
        Assert.Equal(2, await _context.Set<PaymentRefund>().CountAsync());
        Assert.Equal(2, await _context.Payments.CountAsync(p => p.Status == PaymentStatus.Paid));
        Assert.Single((await _service.GetRefundsAsync()).Rows);
        Assert.Single((await _service.GetRefundsAsync(order.CustomerId)).Rows);
        Assert.Empty((await _service.GetRefundsAsync(staff.Id)).Rows);
        Assert.Empty((await _service.GetDashboardAsync()).Rows);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("bad")]
    [InlineData("     ")]
    public async Task RefundRequiresReason(string? reason)
    {
        var (order, staff) = await SeedAsync();
        var paid = await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, staff.Id);
        Assert.False((await _service.RequestRefundAsync(paid.Value!.PaymentId, reason, order.CustomerId)).Succeeded);
        Assert.False((await _service.RecordRefundAsync(paid.Value.PaymentId, reason, staff.Id, true)).Succeeded);
        Assert.Empty(await _context.Set<PaymentRefund>().ToListAsync());
    }
    [Fact]
    public async Task RefundRejectsInactiveAndVoidedAndOverlongReason()
    {
        var (order, admin) = await SeedAsync(RoleNames.Administrator);
        var paid = await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, admin.Id);
        int id = paid.Value!.PaymentId;
        Assert.False((await _service.RecordRefundAsync(id, new string('x',201), admin.Id, true)).Succeeded);
        admin.IsActive = false; await _context.SaveChangesAsync();
        Assert.False((await _service.RecordRefundAsync(id, "Return cash", admin.Id, true)).Succeeded);
        admin.IsActive = true; await _context.SaveChangesAsync();
        Assert.True((await _service.VoidPaymentAsync(id, "Payment mistake", admin.Id)).Succeeded);
        Assert.False((await _service.RequestRefundAsync(id, "Return cash", order.CustomerId)).Succeeded);
        Assert.False((await _service.RecordRefundAsync(id, "Return cash", admin.Id, true)).Succeeded);
    }
    [Fact]
    public async Task RejectedRequestRetainsAuditAndDoesNotReduceNet()
    {
        var (order, staff) = await SeedAsync();
        var paid = await _service.RecordPaymentAsync(order.Id, PaymentMethod.CardTerminal, staff.Id);
        int id = paid.Value!.PaymentId;
        Assert.False((await _service.RejectRefundAsync(id, "No pending request", staff.Id)).Succeeded);
        await _service.RequestRefundAsync(id, "Customer request", order.CustomerId);
        Assert.True((await _service.RejectRefundAsync(id, "Request not approved", staff.Id)).Succeeded);
        var receipt = await _service.GetReceiptForStaffAsync(id);
        Assert.Equal(RefundStatus.Rejected, receipt!.Refund!.Status);
        Assert.Equal(40m, receipt.NetPaid);
        Assert.False((await _service.RecordRefundAsync(id, "Return cash", staff.Id, true)).Succeeded);
    }
    [Fact]
    public async Task NotificationFailureDoesNotUndoRefund()
    {
        var (order, staff) = await SeedAsync();
        var paid = await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, staff.Id);
        _notifications.ThrowOnNotify = true;
        var refunded = await _service.RecordRefundAsync(paid.Value!.PaymentId, "Cash handed back", staff.Id, true);
        Assert.True(refunded.Succeeded);
        Assert.False(refunded.Value!.NotificationSent);
        Assert.Equal(RefundStatus.Completed, (await _context.Set<PaymentRefund>().SingleAsync()).Status);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefundRaceAgainstRefundOrVoidHasOnlyOneWinner(bool voidWins)
    {
        var (order, admin) = await SeedAsync(RoleNames.Administrator);
        var paid = await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, admin.Id);
        int id = paid.Value!.PaymentId;
        var interceptor = new RefundSaveRace(async () => {
            var winner = voidWins ? await _service.VoidPaymentAsync(id, "Void wins race", admin.Id)
                : await _service.RecordRefundAsync(id, "Refund wins race", admin.Id, true);
            Assert.True(winner.Succeeded);
        });
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).AddInterceptors(interceptor).Options;
        using var other = new ApplicationDbContext(opts);
        var loser = new BillingService(other, _notifications, NullLogger<BillingService>.Instance);
        var result = await loser.RecordRefundAsync(id, "Losing refund", admin.Id, true);
        Assert.False(result.Succeeded);
        Assert.Equal(voidWins ? 0 : 1, await _context.Set<PaymentRefund>().CountAsync());
    }
    private class RefundSaveRace(Func<Task> action) : SaveChangesInterceptor
    {
        private bool fired;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if(!fired && data.Context!.ChangeTracker.Entries<PaymentRefund>().Any(e => e.State == EntityState.Added)) { fired = true; await action(); }
            return result;
        }
    }
    [Theory]
    [InlineData(PaymentMethod.Cash)]
    public async Task RefundReportsUseCompletionDateAndIncludeExportRows(PaymentMethod method)
    {
        var (order, staff) = await SeedAsync();
        var paid = await _service.RecordPaymentAsync(order.Id, method, staff.Id);
        int id = paid.Value!.PaymentId;
        DateTime midnight = MalaysiaTime.ToUtc(new DateTime(2026,10,23));
        var payment = await _context.Payments.SingleAsync(); payment.PaidAtUtc = midnight.AddDays(-2);
        await _context.SaveChangesAsync();
        await _service.RequestRefundAsync(id, "Refund requested", order.CustomerId);
        var reports = new ReportService(_context);
        var filter = new ReportFilter { Anchor = new DateOnly(2026,10,23) };
        Assert.Equal(0m, (await reports.GetReportAsync(filter)).Value!.RefundTotal);
        await _service.RecordRefundAsync(id, "Returned full amount", staff.Id, true);
        var refund = await _context.Set<PaymentRefund>().SingleAsync(); refund.ProcessedAtUtc = midnight;
        await _context.SaveChangesAsync();
        var report = (await reports.GetReportAsync(filter)).Value!;
        Assert.Equal(40m, report.RefundTotal); Assert.Equal(-40m, report.NetTotal);
        Assert.Equal(method == PaymentMethod.SimulatedOnline ? 40m : 0m, report.DemoRefundTotal);
        var csv = await new ReportExportService(reports).ExportAsync(filter, ReportExportKind.Sales, ReportExportFormat.Csv);
        string text = System.Text.Encoding.UTF8.GetString(csv.Value!.Content);
        Assert.Contains("Completed refunds", text); Assert.Contains("Returned full amount", text);
        var excel = await new ReportExportService(reports).ExportAsync(filter, ReportExportKind.Sales, ReportExportFormat.Excel);
        using(var book = new ClosedXML.Excel.XLWorkbook(new MemoryStream(excel.Value!.Content))) {
            Assert.Equal(40m, book.Worksheet("Completed refunds").Cell(2,5).GetValue<decimal>());
            Assert.Equal(-40m, book.Worksheet("Refund and net totals").Cell(3,2).GetValue<decimal>());
        }
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var pdf = await new ReportExportService(reports).ExportAsync(filter, ReportExportKind.Sales, ReportExportFormat.Pdf);
        using(var document = UglyToad.PdfPig.PdfDocument.Open(pdf.Value!.Content)) {
            string pdfText = string.Join(" ", document.GetPages().Select(p => p.Text));
            Assert.Contains("Completed refunds", pdfText);
            Assert.Contains("Returned full amount", pdfText);
        }

        refund.ProcessedAtUtc = midnight.AddDays(1); await _context.SaveChangesAsync();
        Assert.Equal(0m, (await reports.GetReportAsync(filter)).Value!.RefundTotal);
    }
}
