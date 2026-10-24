using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rroms.Web.Options;
using Rroms.Web.Services;
using Rroms.Web.Contracts;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Common;
namespace Rroms.Tests.Billing;
public class BillingCalculatorTests
{
    [Theory]
    [InlineData(100,10,6.60,116.60)]
    [InlineData(52.50,5.25,3.47,61.22)]
    [InlineData(0.05,0.01,0,0.06)]
    public void RoundsEachChargeAwayFromZero(decimal subtotal,decimal charge,decimal tax,decimal total)
    {
        var b = new BillingCalculator(Options.Create(new BillingOptions())).Calculate(subtotal);
        Assert.Equal(charge,b.ServiceCharge); Assert.Equal(tax,b.ServiceTax); Assert.Equal(total,b.Total);
    }
    [Fact]
    public void ConfigurableBaseAndZeroRates()
    {
        var o=new BillingOptions { TaxIncludesServiceCharge=false };
        Assert.Equal(116m,new BillingCalculator(Options.Create(o)).Calculate(100m).Total);
        o.ServiceChargePercent=0; o.ServiceTaxPercent=0;
        Assert.Equal(100m,new BillingCalculator(Options.Create(o)).Calculate(100m).Total);
        o.ServiceTaxPercent=6.001m;
        Assert.Throws<ArgumentOutOfRangeException>(()=>new BillingCalculator(Options.Create(o)).Calculate(100m));
    }
}
public partial class BillingServiceTests
{
    [Fact]
    public async Task ChargeSnapshotsSurviveOrderChangesAndAreFullyRefunded()
    {
        var (order,staff)=await SeedAsync();
        order.Subtotal=40;order.ServiceChargePercent=10;order.ServiceCharge=4;
        order.ServiceTaxPercent=6;order.ServiceTax=2.64m;order.TaxIncludesServiceCharge=true;order.TotalAmount=46.64m;
        await _context.SaveChangesAsync();
        var paid=await _service.RecordVisitPaymentAsync(order.ReservationId,PaymentMethod.EWallet,staff.Id);
        Assert.True(paid.Succeeded);
        var saved=await _context.Payments.SingleAsync();
        Assert.Equal(46.64m,saved.Amount);Assert.Equal(2.64m,saved.ServiceTax);
        order.TotalAmount=999;order.ServiceTax=999;await _context.SaveChangesAsync();
        var receipt=await _service.GetReceiptForCustomerAsync(saved.Id,order.CustomerId);
        Assert.Equal(46.64m,receipt!.Bill.TotalAmount);
        Assert.Equal(2.64m,Assert.Single(receipt.Bills).Breakdown.ServiceTax);
        Assert.True((await _service.RecordRefundAsync(saved.Id,"Returned full amount",staff.Id,true)).Succeeded);
        receipt=await _service.GetReceiptForCustomerAsync(saved.Id,order.CustomerId);
        Assert.Equal(46.64m,receipt!.RefundedAmount);Assert.Equal(0m,receipt.NetPaid);
        QuestPDF.Settings.License=QuestPDF.Infrastructure.LicenseType.Community;
        var queue=new Rroms.Web.Services.Email.EmailQueue(Microsoft.Extensions.Logging.Abstractions.NullLogger<Rroms.Web.Services.Email.EmailQueue>.Instance);
        var docs=new ReceiptDocumentService(queue);
        var bytes=docs.Pdf(receipt);
        using(var pdf=UglyToad.PdfPig.PdfDocument.Open(bytes)) {
            string text=string.Join(" ",pdf.GetPages().Select(p=>p.Text));
            Assert.Contains("Service charge (10%)",text);Assert.Contains("Service tax (6%)",text);
            Assert.Contains("RM 2.64",text);Assert.Contains("REFUND COMPLETED",text);
        }
        receipt.CustomerEmail="test@example.invalid";
        Assert.True(docs.QueueEmail(receipt));
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var reader=queue.ReadAllAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Contains("Service tax (6%)",reader.Current.HtmlBody);
        if(Environment.GetEnvironmentVariable("BILLING_QA_DIR") is string dir) {
            Directory.CreateDirectory(dir);await File.WriteAllBytesAsync(Path.Combine(dir,"tax-refund-receipt.pdf"),bytes);
        }

    }
    [Fact]
    public async Task LegacyDemoCannotBeCreatedOrRefundedAndIsExcludedFromReports()
    {
        var (order,staff)=await SeedAsync();
        Assert.False((await _service.RecordPaymentAsync(order.Id,PaymentMethod.SimulatedOnline,staff.Id)).Succeeded);
        Assert.False((await _service.RecordVisitPaymentAsync(order.ReservationId,PaymentMethod.SimulatedOnline,staff.Id)).Succeeded);
        Assert.Empty(await _context.Payments.ToListAsync());
        var p=new Payment {OrderId=order.Id,StaffId=staff.Id,Amount=40,Method=PaymentMethod.SimulatedOnline,Status=PaymentStatus.Paid,PaidAtUtc=DateTime.UtcNow};
        _context.Payments.Add(p);await _context.SaveChangesAsync();
        Assert.False((await _service.RecordRefundAsync(p.Id,"Return old demo",staff.Id,true)).Succeeded);
        _context.Set<PaymentRefund>().Add(new PaymentRefund {PaymentId=p.Id,BatchId=Guid.NewGuid(),Amount=40,Status=RefundStatus.Completed,Reason="Historic demo",RequestedById=staff.Id,RequestedAtUtc=DateTime.UtcNow,ProcessedAtUtc=DateTime.UtcNow,ProcessedById=staff.Id});
        await _context.SaveChangesAsync();
        var report=(await new ReportService(_context).GetReportAsync(new ReportFilter {Anchor=DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow))})).Value!;
        Assert.Equal(0,report.PaidCount);Assert.Equal(0m,report.PaidTotal);Assert.Equal(0m,report.RefundTotal);Assert.Empty(report.PopularItems);
        Assert.DoesNotContain(report.SalesMethods,x=>x.Method==PaymentMethod.SimulatedOnline);
        Assert.Equal(0m,(await _service.GetDashboardAsync()).PaidToday);
        Assert.Empty((await _service.GetDashboardAsync()).Rows); // historical paid orders are not charged again
    }
}
