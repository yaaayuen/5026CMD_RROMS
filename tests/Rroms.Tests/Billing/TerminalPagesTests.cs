using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Services;

namespace Rroms.Tests.Billing;

/// <summary>HTTP tests for the simulated terminal: approval, decline and the two slip copies.</summary>
public partial class BillingPagesTests
{
    /// <summary>A card payment opens a receipt with the approval code and both terminal slip copies.</summary>
    [Fact]
    public async Task Terminal_CardPayment_ReceiptAndSlipsShowApprovalCode()
    {
        var (client, _) = await SignInAsync(RoleNames.Staff);
        using (client)
        {
            Order order = await AddOrderAsync();
            string url = $"{BillingUrl}/Details/{order.Id}";
            Assert.Contains("Test mode: make the terminal decline", await client.GetStringAsync(url));

            var response = await FormHelper.PostFormAsync(client, url, url,
                new Dictionary<string, string> { ["Input.Method"] = "CardTerminal" });

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            string receiptUrl = response.Headers.Location!.ToString();
            Payment payment;
            using (IServiceScope scope = _factory.Services.CreateScope())
            {
                payment = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .Payments.SingleAsync(p => p.OrderId == order.Id);
            }
            string receipt = await client.GetStringAsync(receiptUrl);
            Assert.Contains("Approval code", receipt);
            Assert.Contains(payment.ApprovalCode!, receipt);
            Assert.Contains(payment.TerminalReference!, receipt);

            string merchant = await client.GetStringAsync($"{BillingUrl}/Slip/{payment.Id}/merchant");
            Assert.Contains("MERCHANT COPY", merchant);
            Assert.Contains(SimulatedPaymentTerminal.CardAcquirer, merchant);
            Assert.Contains("**** **** **** " + payment.CardLast4, merchant);
            string customer = await client.GetStringAsync($"{BillingUrl}/Slip/{payment.Id}/customer");
            Assert.Contains("CUSTOMER COPY", customer);
            Assert.Contains(payment.ApprovalCode!, customer);
        }
    }

    /// <summary>A declined terminal keeps staff on the bill with a clear message and saves nothing.</summary>
    [Fact]
    public async Task Terminal_DeclinedPayment_ShowsMessageAndSavesNothing()
    {
        var (client, _) = await SignInAsync(RoleNames.Staff);
        using (client)
        {
            Order order = await AddOrderAsync();
            string url = $"{BillingUrl}/Details/{order.Id}";

            var response = await FormHelper.PostFormAsync(client, url, url, new Dictionary<string, string>
            {
                ["Input.Method"] = "EWallet",
                ["Input.SimulateDecline"] = "true"
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("The terminal declined this payment", await response.Content.ReadAsStringAsync());
            using IServiceScope scope = _factory.Services.CreateScope();
            Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Payments.AnyAsync(p => p.OrderId == order.Id));
        }
    }

    /// <summary>Cash has no terminal slip, and customers cannot open slips.</summary>
    [Fact]
    public async Task Terminal_SlipForCashIsNotFoundAndCustomersAreDenied()
    {
        var (client, _) = await SignInAsync(RoleNames.Staff);
        using (client)
        {
            Order order = await AddOrderAsync();
            string url = $"{BillingUrl}/Details/{order.Id}";
            var response = await FormHelper.PostFormAsync(client, url, url,
                new Dictionary<string, string> { ["Input.Method"] = "Cash", ["Input.CashReceived"] = "100" });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            int paymentId = int.Parse(response.Headers.Location!.ToString().Split('/').Last());
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{BillingUrl}/Slip/{paymentId}/merchant")).StatusCode);
        }
        var (customer, _) = await SignInAsync(RoleNames.Customer);
        using (customer)
        {
            var denied = await customer.GetAsync($"{BillingUrl}/Slip/1/customer");
            Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
            Assert.Contains("/Account/AccessDenied", denied.Headers.Location?.ToString());
        }
    }
}
