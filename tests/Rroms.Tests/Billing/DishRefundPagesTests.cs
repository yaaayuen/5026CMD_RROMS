using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.Billing;

public partial class BillingPagesTests
{
    /// <summary>Staff choose one dish on the receipt; the receipt then lists the refunded dish and the net paid.</summary>
    [Fact]
    public async Task DishRefund_StaffChoosesDish_ReceiptShowsRefundLine()
    {
        var (staff, _) = await SignInAsync(RoleNames.Staff);
        using (staff)
        {
            Order order = await AddOrderAsync();
            string bill = $"{BillingUrl}/Details/{order.Id}";
            var paid = await FormHelper.PostFormAsync(staff, bill, bill, new Dictionary<string, string> { ["Input.Method"] = "Cash" });
            string receipt = paid.Headers.Location!.ToString();
            string html = await staff.GetStringAsync(receipt);
            Assert.Contains("Refund quantity", html);
            int itemId;
            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                itemId = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .OrderItems.Where(i => i.OrderId == order.Id).Select(i => i.Id).SingleAsync();
            }

            var refund = await FormHelper.PostFormAsync(staff, receipt, receipt + "?handler=Refund", new Dictionary<string, string>
            {
                ["RefundReason"] = "One portion was burnt",
                ["MoneyReturned"] = "true",
                ["RefundLines[0].OrderItemId"] = itemId.ToString(),
                ["RefundLines[0].Quantity"] = "1"
            });

            Assert.Equal(HttpStatusCode.Redirect, refund.StatusCode);
            string after = await staff.GetStringAsync(receipt);
            Assert.Contains("× 1", after);
            Assert.Contains("Net paid after refund: RM 20.00", after);
        }
    }

    /// <summary>Posting the dish table with nothing chosen is refused.</summary>
    [Fact]
    public async Task DishRefund_NothingChosen_ShowsError()
    {
        var (staff, _) = await SignInAsync(RoleNames.Staff);
        using (staff)
        {
            Order order = await AddOrderAsync();
            string bill = $"{BillingUrl}/Details/{order.Id}";
            var paid = await FormHelper.PostFormAsync(staff, bill, bill, new Dictionary<string, string> { ["Input.Method"] = "Cash" });
            string receipt = paid.Headers.Location!.ToString();
            int itemId;
            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                itemId = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .OrderItems.Where(i => i.OrderId == order.Id).Select(i => i.Id).SingleAsync();
            }

            var refund = await FormHelper.PostFormAsync(staff, receipt, receipt + "?handler=Refund", new Dictionary<string, string>
            {
                ["RefundReason"] = "Nothing chosen here",
                ["MoneyReturned"] = "true",
                ["RefundLines[0].OrderItemId"] = itemId.ToString(),
                ["RefundLines[0].Quantity"] = "0"
            });

            Assert.Equal(HttpStatusCode.OK, refund.StatusCode);
            Assert.Contains("Choose at least one dish", await refund.Content.ReadAsStringAsync());
        }
    }
}
