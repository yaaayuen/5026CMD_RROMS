using Microsoft.EntityFrameworkCore;
using Rroms.Web.Contracts;
using Rroms.Web.Models;
using Rroms.Web.Services;

namespace Rroms.Tests.Billing;

/// <summary>Card / e-wallet payments go through the (simulated) terminal and keep its slip details.</summary>
public partial class BillingServiceTests
{
    /// <summary>An approved card payment stores the slip numbers once and shows them on the receipt.</summary>
    [Fact]
    public async Task Terminal_CardApproved_SavesSlipAndShowsItOnReceipt()
    {
        var (order, staff) = await SeedAsync();

        var result = await _service.RecordPaymentAsync(order.Id, PaymentMethod.CardTerminal, staff.Id);

        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        Payment payment = await _context.Payments.SingleAsync();
        Assert.Matches("^[A-HJ-NP-Z2-9]{6}$", payment.ApprovalCode);
        Assert.Matches("^[0-9]{12}$", payment.TerminalReference);
        Assert.Matches("^[0-9]{4}$", payment.CardLast4);
        Assert.Contains(payment.CardBrand, new[] { "Visa", "Mastercard" });
        Assert.Contains(payment.CardType, new[] { "Credit", "Debit" });
        Assert.Equal(SimulatedPaymentTerminal.CardAcquirer, payment.Acquirer);
        Assert.Null(payment.CashReceived);
        var receipt = await _service.GetReceiptForCustomerAsync(payment.Id, order.CustomerId);
        Assert.NotNull(receipt!.Terminal);
        Assert.Equal(payment.ApprovalCode, receipt.Terminal!.ApprovalCode);
        Assert.Equal(payment.TerminalReference, receipt.Terminal.Reference);
    }

    /// <summary>An e-wallet payment keeps a transaction ID but no card details.</summary>
    [Fact]
    public async Task Terminal_EWallet_SavesTransactionIdWithoutCardDetails()
    {
        var (order, staff) = await SeedAsync();

        var result = await _service.RecordPaymentAsync(order.Id, PaymentMethod.EWallet, staff.Id);

        Assert.True(result.Succeeded);
        Payment payment = await _context.Payments.SingleAsync();
        Assert.Matches("^[0-9]{16}$", payment.TerminalReference);
        Assert.Null(payment.ApprovalCode);
        Assert.Null(payment.CardLast4);
        Assert.Equal(SimulatedPaymentTerminal.WalletNetwork, payment.Acquirer);
    }

    /// <summary>A declined card saves nothing, sends nothing, and the order can still be paid another way.</summary>
    [Fact]
    public async Task Terminal_Declined_SavesNothingAndOrderStaysUnpaid()
    {
        var (order, staff) = await SeedAsync();

        var declined = await _service.RecordPaymentAsync(order.Id, PaymentMethod.CardTerminal, staff.Id, simulateDecline: true);

        Assert.False(declined.Succeeded);
        Assert.Contains(BillingService.TerminalDeclinedMessage, declined.Errors);
        Assert.Equal(0, await _context.Payments.CountAsync());
        Assert.Equal(0, _notifications.Count);
        var cash = await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, staff.Id);
        Assert.True(cash.Succeeded);
    }

    /// <summary>Cash never touches the terminal.</summary>
    [Fact]
    public async Task Terminal_Cash_HasNoSlip()
    {
        var (order, staff) = await SeedAsync();

        var result = await _service.RecordPaymentAsync(order.Id, PaymentMethod.Cash, staff.Id, 50m, simulateDecline: true);

        Assert.True(result.Succeeded);
        Payment payment = await _context.Payments.SingleAsync();
        Assert.Null(payment.TerminalReference);
        Assert.Null((await _service.GetReceiptForStaffAsync(payment.Id))!.Terminal);
    }

    /// <summary>A whole-visit card payment is one terminal transaction: one slip for the combined receipt.</summary>
    [Fact]
    public async Task Terminal_VisitPayment_OneSlipForTheWholeVisit()
    {
        var (order, staff) = await SeedAsync();
        await AddSecondOrderAsync(order, OrderStatus.Completed, 30m);

        var declined = await _service.RecordVisitPaymentAsync(order.ReservationId, PaymentMethod.CardTerminal, staff.Id, simulateDecline: true);
        Assert.False(declined.Succeeded);
        Assert.Equal(0, await _context.Payments.CountAsync());

        var result = await _service.RecordVisitPaymentAsync(order.ReservationId, PaymentMethod.CardTerminal, staff.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(1, await _context.Payments.CountAsync(p => p.TerminalReference != null));
        var receipt = await _service.GetReceiptForStaffAsync(result.Value!.PaymentId);
        Assert.NotNull(receipt!.Terminal);
        Assert.Equal(70m, receipt.TotalPaid);
    }

    /// <summary>The demo terminal's codes avoid look-alike characters and never repeat in practice.</summary>
    [Fact]
    public void SimulatedTerminal_CodesAreReadableAndVary()
    {
        var terminal = new SimulatedPaymentTerminal();
        var codes = Enumerable.Range(0, 200)
            .Select(_ => terminal.Charge(new TerminalRequest { Amount = 10m, Method = PaymentMethod.CardTerminal }))
            .ToList();

        Assert.All(codes, slip => Assert.True(slip.Approved));
        Assert.All(codes, slip => Assert.DoesNotMatch("[01OI]", slip.ApprovalCode!));
        Assert.True(codes.Select(slip => slip.Reference).Distinct().Count() > 190);
        Assert.False(terminal.Charge(new TerminalRequest { Amount = 0m, Method = PaymentMethod.EWallet }).Approved);
        // In Production the test-mode decline is ignored.
        var production = new SimulatedPaymentTerminal(allowTestDeclines: false);
        Assert.True(production.Charge(new TerminalRequest { Amount = 10m, Method = PaymentMethod.CardTerminal, SimulateDecline = true }).Approved);
    }
}
