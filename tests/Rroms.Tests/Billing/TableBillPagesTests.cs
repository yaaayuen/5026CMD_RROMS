using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.Billing;

/// <summary>One bill per table: staff press one "Take payment" and every unpaid order of the visit is shown.</summary>
public partial class BillingPagesTests
{
    [Fact]
    public async Task TableBill_UnpaidListShowsOneRowAndOneButtonPerVisit()
    {
        var (staff, _) = await SignInAsync(RoleNames.Staff);
        using (staff)
        {
            Order first = await AddOrderAsync();
            Order second = await AddOrderToSameVisitAsync(first);

            string list = await staff.GetStringAsync(BillingUrl);

            Assert.Contains($"#{first.Id}, #{second.Id}", list);
            Assert.Contains("2 orders · one bill", list);
            Assert.Contains($"href=\"/Staff/Billing/Visit/{first.ReservationId}\"", list);
            Assert.DoesNotContain("Bill whole visit", list);
            Assert.DoesNotContain($"href=\"/Staff/Billing/Details/{first.Id}\"", list);

            string visit = await staff.GetStringAsync($"{BillingUrl}/Visit/{first.ReservationId}");
            Assert.Contains($"Order #{first.Id}", visit);
            Assert.Contains($"Order #{second.Id}", visit);
            Assert.Contains("Visit total: RM 70.00", visit);
            Assert.Contains("billing-columns", visit);
        }
    }

    [Fact]
    public async Task TableBill_SingleOrderPageSendsStaffToTheTableBill()
    {
        var (staff, _) = await SignInAsync(RoleNames.Staff);
        using (staff)
        {
            Order first = await AddOrderAsync();
            await AddOrderToSameVisitAsync(first);

            string html = await staff.GetStringAsync($"{BillingUrl}/Details/{first.Id}");

            Assert.Contains("This table has 2 unpaid orders", html);
            Assert.Contains($"href=\"/Staff/Billing/Visit/{first.ReservationId}\"", html);
            Assert.DoesNotContain("name=\"Input.Method\"", html);
        }
    }

    /// <summary>A second completed RM 30 order on the same reservation.</summary>
    private async Task<Order> AddOrderToSameVisitAsync(Order first)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        int dishId = await db.OrderItems.Where(i => i.OrderId == first.Id).Select(i => i.MenuItemId).FirstAsync();
        var second = new Order
        {
            ReservationId = first.ReservationId, CustomerId = first.CustomerId, Status = OrderStatus.Completed,
            TotalAmount = 30m, PlacedAtUtc = first.PlacedAtUtc.AddMinutes(20), UpdatedAtUtc = first.PlacedAtUtc.AddMinutes(20)
        };
        second.Items.Add(new OrderItem { MenuItemId = dishId, Quantity = 1, UnitPrice = 30m });
        db.Orders.Add(second);
        await db.SaveChangesAsync();
        return second;
    }
}
