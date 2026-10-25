using Microsoft.EntityFrameworkCore;
using Rroms.Tests.Menu;
using Rroms.Web.Contracts;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Tests.Billing;

/// <summary>Dish-level (partial) refunds: only the chosen dishes are refunded and the kitchen order is untouched.</summary>
public partial class BillingServiceTests
{
    /// <summary>The example from the brief: RM 25 bill, Tofu (RM 5) refunded, RM 20 net paid.</summary>
    [Fact]
    public async Task DishRefund_OneDish_ReceiptShowsDishAndNetPaid()
    {
        var (order, staff) = await SeedAsync();
        var (meal, tofu, _) = await AddThreeDishOrderAsync(order, 25m);
        int paymentId = (await _service.RecordPaymentAsync(meal.Id, PaymentMethod.Cash, staff.Id)).Value!.PaymentId;

        var result = await _service.RecordRefundAsync(paymentId, "Tofu was cold", staff.Id, true,
            new[] { new RefundLineInput { OrderItemId = tofu.Id, Quantity = 1 } });

        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        var receipt = await _service.GetReceiptForStaffAsync(paymentId);
        Assert.Equal(RefundStatus.Completed, receipt!.Refund!.Status);
        Assert.Equal(5m, receipt.Refund.Amount);
        Assert.Equal(20m, receipt.NetPaid);
        var line = Assert.Single(receipt.Refund.Lines);
        Assert.Equal(("Tofu", 1, 5m), (line.DishName, line.Quantity, line.Amount));
        // The kitchen's order is not changed by a refund.
        _context.ChangeTracker.Clear();
        Order kitchen = await _context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == meal.Id);
        Assert.Equal(3, kitchen.Items.Count);
        Assert.Equal(25m, kitchen.TotalAmount);
        Assert.Equal(PaymentStatus.Paid, (await _context.Payments.SingleAsync(p => p.Id == paymentId)).Status);
    }

    /// <summary>A dish's refund includes its share of service charge and tax; refunding everything returns exactly what was paid.</summary>
    [Fact]
    public async Task DishRefund_ShareIncludesChargesAndEverythingIsExact()
    {
        var (order, staff) = await SeedAsync();
        // Items RM 25 plus charges: the customer paid RM 29.
        var (meal, tofu, others) = await AddThreeDishOrderAsync(order, 29m);
        int paymentId = (await _service.RecordPaymentAsync(meal.Id, PaymentMethod.Cash, staff.Id)).Value!.PaymentId;
        var all = new[] { tofu }.Concat(others)
            .Select(i => new RefundLineInput { OrderItemId = i.Id, Quantity = i.Quantity }).ToList();

        var result = await _service.RecordRefundAsync(paymentId, "Whole meal was wrong", staff.Id, true, all);

        Assert.True(result.Succeeded);
        var receipt = await _service.GetReceiptForStaffAsync(paymentId);
        Assert.Equal(29m, receipt!.Refund!.Amount);
        Assert.Equal(29m, receipt.Refund.Lines.Sum(l => l.Amount));
        Assert.Equal(5.80m, receipt.Refund.Lines.Single(l => l.DishName == "Tofu").Amount);
        Assert.Equal(0m, receipt.NetPaid);
    }

    /// <summary>Unknown dishes, too many, or nothing chosen are refused and nothing is saved.</summary>
    [Fact]
    public async Task DishRefund_InvalidChoices_AreRefused()
    {
        var (order, staff) = await SeedAsync();
        var (meal, tofu, _) = await AddThreeDishOrderAsync(order, 25m);
        int paymentId = (await _service.RecordPaymentAsync(meal.Id, PaymentMethod.Cash, staff.Id)).Value!.PaymentId;
        int otherOrderItem = order.Items.Single().Id;

        var tooMany = await _service.RecordRefundAsync(paymentId, "Too many", staff.Id, true,
            new[] { new RefundLineInput { OrderItemId = tofu.Id, Quantity = 2 } });
        var notOnReceipt = await _service.RecordRefundAsync(paymentId, "Wrong dish", staff.Id, true,
            new[] { new RefundLineInput { OrderItemId = otherOrderItem, Quantity = 1 } });
        var nothing = await _service.RecordRefundAsync(paymentId, "Nothing chosen", staff.Id, true,
            new[] { new RefundLineInput { OrderItemId = tofu.Id, Quantity = 0 } });

        Assert.Contains(BillingService.InvalidRefundLineMessage, tooMany.Errors);
        Assert.Contains(BillingService.InvalidRefundLineMessage, notOnReceipt.Errors);
        Assert.Contains(BillingService.NoRefundLinesMessage, nothing.Errors);
        Assert.Equal(0, await _context.Set<PaymentRefund>().CountAsync());
    }

    /// <summary>A customer's request is decided by staff, who refund only the affected dish; a receipt is refunded once.</summary>
    [Fact]
    public async Task DishRefund_AfterCustomerRequest_OnlyOnce()
    {
        var (order, staff) = await SeedAsync();
        var (meal, tofu, others) = await AddThreeDishOrderAsync(order, 25m);
        int paymentId = (await _service.RecordPaymentAsync(meal.Id, PaymentMethod.CardTerminal, staff.Id)).Value!.PaymentId;
        Assert.True((await _service.RequestRefundAsync(paymentId, "The tofu tasted off", meal.CustomerId)).Succeeded);

        var result = await _service.RecordRefundAsync(paymentId, "Checked: tofu refunded", staff.Id, true,
            new[] { new RefundLineInput { OrderItemId = tofu.Id, Quantity = 1 } });
        var again = await _service.RecordRefundAsync(paymentId, "Second refund", staff.Id, true,
            new[] { new RefundLineInput { OrderItemId = others[0].Id, Quantity = 1 } });

        Assert.True(result.Succeeded);
        Assert.False(again.Succeeded);
        PaymentRefund saved = await _context.Set<PaymentRefund>().Include(r => r.Lines).SingleAsync();
        Assert.Equal((RefundStatus.Completed, 5m), (saved.Status, saved.Amount));
        Assert.Single(saved.Lines);
    }

    /// <summary>On a whole-visit receipt, refunding a dish of one order leaves the other order's payment whole.</summary>
    [Fact]
    public async Task DishRefund_VisitReceipt_RefundsOnlyTheChosenOrder()
    {
        var (order, staff) = await SeedAsync();
        var (meal, tofu, _) = await AddThreeDishOrderAsync(order, 25m);
        int receiptId = (await _service.RecordVisitPaymentAsync(order.ReservationId, PaymentMethod.Cash, staff.Id)).Value!.PaymentId;

        var result = await _service.RecordRefundAsync(receiptId, "Tofu was cold", staff.Id, true,
            new[] { new RefundLineInput { OrderItemId = tofu.Id, Quantity = 1 } });

        Assert.True(result.Succeeded);
        var receipt = await _service.GetReceiptForStaffAsync(receiptId);
        Assert.Equal(65m, receipt!.TotalPaid);
        Assert.Equal(5m, receipt.Refund!.Amount);
        Assert.Equal(60m, receipt.NetPaid);
        var rows = await _context.Set<PaymentRefund>().Include(r => r.Payment).ToListAsync();
        Assert.Equal(0m, rows.Single(r => r.Payment!.OrderId == order.Id).Amount);
        Assert.Equal(5m, rows.Single(r => r.Payment!.OrderId == meal.Id).Amount);
    }

    /// <summary>A second completed order on the same visit: Tofu RM 5, Chicken chop RM 10, Mee RM 10.</summary>
    private async Task<(Order Meal, OrderItem Tofu, List<OrderItem> Others)> AddThreeDishOrderAsync(Order first, decimal paid)
    {
        MenuItem tofu = await OrderTestData.AddDishAsync(_context, "Tofu");
        MenuItem chop = await OrderTestData.AddDishAsync(_context, "Chicken chop");
        MenuItem mee = await OrderTestData.AddDishAsync(_context, "Mee");
        var meal = new Order
        {
            ReservationId = first.ReservationId, CustomerId = first.CustomerId, Status = OrderStatus.Completed,
            TotalAmount = paid, PlacedAtUtc = first.PlacedAtUtc.AddMinutes(10), UpdatedAtUtc = first.PlacedAtUtc.AddMinutes(10)
        };
        meal.Items.Add(new OrderItem { MenuItemId = tofu.Id, Quantity = 1, UnitPrice = 5m });
        meal.Items.Add(new OrderItem { MenuItemId = chop.Id, Quantity = 1, UnitPrice = 10m });
        meal.Items.Add(new OrderItem { MenuItemId = mee.Id, Quantity = 1, UnitPrice = 10m });
        _context.Orders.Add(meal);
        await _context.SaveChangesAsync();
        var items = meal.Items.OrderBy(i => i.Id).ToList();
        return (meal, items[0], items.Skip(1).ToList());
    }
}
