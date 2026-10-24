using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
namespace Rroms.Tests.Billing;
public partial class BillingPagesTests
{
    [Fact]
    public async Task CustomerRequestsOwnRefundStaffRecordsAndReceiptPdfShowsRefund()
    {
        var (customer, owner) = await SignInAsync(RoleNames.Customer);
        var (stranger, _) = await SignInAsync(RoleNames.Customer);
        var (staff, _) = await SignInAsync(RoleNames.Staff);
        using(customer) using(stranger) using(staff) {
            var order = await AddOrderAsync(owner.Id);
            string bill = $"/Staff/Billing/Details/{order.Id}";
            var response = await FormHelper.PostFormAsync(staff, bill, bill, new Dictionary<string,string> { ["Input.Method"] = "Cash" });
            string staffReceipt = response.Headers.Location!.ToString();
            int id = int.Parse(staffReceipt.Split('/').Last());
            string receipt = $"/Customer/Receipts/Details/{id}";
            var foreignPost = await FormHelper.PostFormAsync(stranger, "/Customer/Refunds", receipt + "?handler=RequestRefund",
                new Dictionary<string,string> { ["RefundReason"] = "Not my receipt" });
            Assert.Equal(HttpStatusCode.NotFound, foreignPost.StatusCode);
            var request = await FormHelper.PostFormAsync(customer, receipt, receipt + "?handler=RequestRefund",
                new Dictionary<string,string> { ["RefundReason"] = "Customer refund reason", ["Amount"] = "9999" });
            Assert.Equal(HttpStatusCode.Redirect, request.StatusCode);
            Assert.Contains("REFUND REQUESTED", await customer.GetStringAsync(receipt));
            Assert.Contains($"/Customer/Receipts/Details/{id}", await customer.GetStringAsync("/Customer/Refunds"));
            Assert.DoesNotContain($"/Customer/Receipts/Details/{id}", await stranger.GetStringAsync("/Customer/Refunds"));
            Assert.Contains($"/Staff/Billing/Receipt/{id}", await staff.GetStringAsync("/Staff/Billing/Refunds"));
            var refund = await FormHelper.PostFormAsync(staff, staffReceipt, staffReceipt + "?handler=Refund",
                new Dictionary<string,string> { ["RefundReason"] = "Returned to customer", ["MoneyReturned"] = "true", ["Amount"] = "9999" });
            Assert.Equal(HttpStatusCode.Redirect, refund.StatusCode);
            Assert.Contains("REFUNDED", await customer.GetStringAsync(receipt));
            var pdf = await customer.GetByteArrayAsync(receipt + "?handler=Pdf");
            using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
            Assert.Contains("REFUND COMPLETED", doc.GetPage(1).Text);
            if(Environment.GetEnvironmentVariable("BILLING_QA_DIR") is string qa) {
                Directory.CreateDirectory(qa);
                await File.WriteAllBytesAsync(Path.Combine(qa, "refund-receipt.pdf"), pdf);
                await File.WriteAllTextAsync(Path.Combine(qa, "refund-receipt.html"), await customer.GetStringAsync(receipt));
            }

            Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(receipt + "?handler=Pdf")).StatusCode);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var saved = await db.Set<PaymentRefund>().SingleAsync(r => r.PaymentId == id);
            Assert.Equal(40m, saved.Amount);
            Assert.Equal(RefundStatus.Completed, saved.Status);
        }
    }
    [Theory]
    [InlineData("/Staff/Billing/Refunds")]
    [InlineData("/Customer/Refunds")]
    public async Task RefundHistoryRequiresLogin(string url)
    {
        using var client = _factory.CreateSecureClient();
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(url)).StatusCode);
    }
}
