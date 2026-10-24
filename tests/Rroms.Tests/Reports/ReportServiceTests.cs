using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.Infrastructure;
using Rroms.Tests.Menu;
using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Tests.Reports;
/// <summary>Known-data reports and DA-2 reconciliation.</summary>
public class ReportServiceTests
{
    /// <summary>Tests exact Malaysia boundaries, demo split, snapshots and independent activity counts.</summary>
    [Fact]
    public async Task KnownData_ReconcilesAndUsesIndependentDates()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dish = await OrderTestData.AddDishAsync(db, "<script>dish</script>", 999m);
        // DA-4 integration: retain the quantity tie while respecting YJ's unique-name constraint.
        var secondDish = await OrderTestData.AddDishAsync(db, "<script>second dish</script>", 999m);
        DateTime start = MalaysiaTime.ToUtc(new DateTime(2026, 10, 20));
        var orders = new List<Order>();
        for (int i = 0; i < 6; i++)
        {
            var order = await OrderTestData.AddOrderAsync(db, i == 1 ? secondDish.Id : dish.Id, 2, 10m);
            order.PlacedAtUtc = start.AddDays(-1);
            var reservation = (await db.Reservations.FindAsync(order.ReservationId))!;
            reservation.StartUtc = start.AddDays(-1);
            reservation.EndUtc = reservation.StartUtc.AddHours(2);
            orders.Add(order);
        }
        orders[0].PlacedAtUtc = start;
        var booking = (await db.Reservations.FindAsync(orders[0].ReservationId))!;
        booking.StartUtc = start; booking.EndUtc = start.AddHours(2); booking.Status = ReservationStatus.Cancelled;
        // Two independent reservations for the same customer must not multiply payments.
        var extra = await OrderTestData.AddReservationAsync(db, orders[0].CustomerId);
        extra.StartUtc = start; extra.EndUtc = start.AddHours(2);
        for (int i = 0; i < orders.Count; i++)
        {
            db.Payments.Add(new Payment
            {
                OrderId = orders[i].Id, StaffId = orders[0].CustomerId, Amount = 20m,
                Method = i == 1 ? PaymentMethod.EWallet : PaymentMethod.Cash,
                Status = i == 2 ? PaymentStatus.Voided : i == 3 ? PaymentStatus.Unpaid : PaymentStatus.Paid,
                PaidAtUtc = i == 1 ? start.AddDays(1).AddTicks(-1) : i == 4 ? start.AddTicks(-1) : i == 5 ? start.AddDays(1) : start
            });
        }
        await db.SaveChangesAsync();
        var filter = new ReportFilter { Anchor = new DateOnly(2026, 10, 20) };
        var result = await scope.ServiceProvider.GetRequiredService<IReportService>().GetReportAsync(filter);
        Assert.True(result.Succeeded);
        var r = result.Value!;
        Assert.Equal(2, r.ReservationCount); Assert.Equal(4, r.GuestCount);
        Assert.Equal(1, r.Reservations.Single(x => x.Status == ReservationStatus.Cancelled).Count);
        Assert.Equal(2, r.PaidCount); Assert.Equal(40m, r.PaidTotal);
        Assert.Equal(0m, r.DemoPaidTotal); Assert.Equal(40m, r.NonDemoPaidTotal);
        Assert.Equal(40m, Assert.Single(r.DailySales).PaidAmount);
        Assert.Equal(2, r.PopularItems.Count);
        Assert.Equal(dish.Id, r.PopularItems[0].MenuItemId);
        Assert.Equal(1, r.PopularItems[0].Rank); Assert.Equal(2, r.PopularItems[1].Rank);
        Assert.All(r.PopularItems, x => Assert.Equal(20m, x.LineValue));
        Assert.Equal(0, r.PopularItems[1].DemoQuantity);
        Assert.Equal(2, r.Customers.Count);
        var first = r.Customers.Single(x => x.CustomerId == orders[0].CustomerId);
        Assert.Equal(2, first.ReservationCount); Assert.Equal(1, first.OrderCount); Assert.Equal(1, first.PaidCount);
        Assert.Equal(0, r.Customers.Single(x => x.CustomerId == orders[1].CustomerId).OrderCount);
        var monitoring = await scope.ServiceProvider.GetRequiredService<IMonitoringService>().GetTransactionMonitoringAsync(
            new TransactionMonitoringFilter { From = filter.Anchor, To = filter.Anchor, Status = PaymentStatus.Paid });
        Assert.True(monitoring.Succeeded);
        Assert.Equal(monitoring.Value!.Count, r.PaidCount);
        Assert.Equal(monitoring.Value.PaidTotal, r.PaidTotal);
        Assert.Equal(monitoring.Value.DemoPaidTotal, r.DemoPaidTotal);
        Assert.Equal(monitoring.Value.NonDemoPaidTotal, r.NonDemoPaidTotal);
    }
    /// <summary>An empty week still returns seven daily rows and invalid input returns a business failure.</summary>
    [Fact]
    public async Task EmptyAndInvalid()
    {
        using var factory = new TestWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportService>();
        var r = (await service.GetReportAsync(new ReportFilter { Period = ReportPeriod.Weekly, Anchor = new DateOnly(2030, 1, 1) })).Value!;
        Assert.Equal(7, r.DailySales.Count); Assert.Equal(0m, r.PaidTotal);
        Assert.Empty(r.Customers); Assert.Empty(r.PopularItems);
        Assert.False((await service.GetReportAsync(new ReportFilter { Period = (ReportPeriod)999 })).Succeeded);
    }
}
