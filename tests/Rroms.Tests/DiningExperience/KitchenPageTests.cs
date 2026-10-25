using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.History;
using Rroms.Tests.Infrastructure;
using Rroms.Tests.Menu;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
namespace Rroms.Tests.DiningExperience;

/// <summary>Part 15 through the real pages: the automatic kitchen display and the customer's countdown.</summary>
public class KitchenPageTests
{
    [Fact]
    public async Task KitchenShowsCountdownsQueueAndOnlyServeOrCancelButtons()
    {
        using var factory = new TestWebApplicationFactory();
        ApplicationUser guest = await factory.CreateUserAsync("alex-kitchen@test.local", "Test!Customer2026", RoleNames.Customer);
        for (int n = 0; n < 5; n++) { await AddOrderAsync(factory, guest.Id, 12, placedMinutesAgo: 30 - n); }
        using var staff = factory.CreateSecureClient();
        await FormHelper.LoginAsync(staff, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        string html = await staff.GetStringAsync("/Staff/Orders?handler=Queue");

        Assert.Contains("Cooking now", html);
        Assert.Contains(">4 / 4</span>", html);
        Assert.Equal(4, Regex.Matches(html, "data-ends-at=\"").Count);
        Assert.Contains("#1 in line", html);
        Assert.Contains("starts in about", html);
        Assert.Contains("(12 min)", html);
        Assert.Contains("Cancel", html);
        // Starting and finishing cooking are automatic, so staff never get those buttons.
        Assert.DoesNotContain("name=\"next\" value=\"Preparing\"", html);
        Assert.DoesNotContain("name=\"next\" value=\"ReadyToServe\"", html);
        Assert.Matches("data-order-ids=\"\\d+(,\\d+){4}\"", html);
    }

    [Fact]
    public async Task CustomerSeesTheirPlaceInLineAndTheCookingCountdown()
    {
        using var factory = new TestWebApplicationFactory();
        ApplicationUser guest = await factory.CreateUserAsync("alex-eta@test.local", "Test!Customer2026", RoleNames.Customer);
        ApplicationUser other = await factory.CreateUserAsync("alex-eta-other@test.local", "Test!Customer2026", RoleNames.Customer);
        for (int n = 0; n < 4; n++) { await AddOrderAsync(factory, other.Id, 10, placedMinutesAgo: 40 - n); }
        int mine = await AddOrderAsync(factory, guest.Id, 15, placedMinutesAgo: 5);
        using var client = factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, "alex-eta@test.local", "Test!Customer2026");

        string queued = await client.GetStringAsync("/Customer/Tracking?handler=Status");
        Assert.Contains("In the kitchen queue: you are next.", queued);
        Assert.Contains("Cooking starts in about", queued);
        Assert.Contains("ready around", queued);

        // Once the other orders finish, the customer's own order starts and shows a live countdown.
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.KitchenTickets.ExecuteUpdateAsync(s => s.SetProperty(t => t.ReadyAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        }
        string cooking = await client.GetStringAsync("/Customer/Tracking?handler=Status");
        Assert.Contains("Cooking now. Ready in", cooking);
        Assert.Contains("data-ends-at=\"", cooking);
        Assert.DoesNotContain("In the kitchen queue", cooking);
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(OrderStatus.Preparing, (await db.Orders.FindAsync(mine))!.Status);
        }
    }

    [Fact]
    public async Task AdminCanSaveACookingTimeOnADish()
    {
        using var factory = new TestWebApplicationFactory();
        using var admin = factory.CreateSecureClient();
        await FormHelper.LoginAsync(admin, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        string form = await admin.GetStringAsync("/Admin/Menu/Edit");
        Assert.Contains("Cooking time (minutes)", form);
        Assert.Contains("Leave empty for drinks", form);
    }

    private static async Task<int> AddOrderAsync(TestWebApplicationFactory factory, string customerId, int cookingMinutes, int placedMinutesAgo)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        MenuItem dish = await OrderTestData.AddDishAsync(db, "Kitchen page dish");
        dish.CookingMinutes = cookingMinutes;
        Reservation visit = await HistoryTestData.AddReservationAsync(db, customerId, DateTime.UtcNow.AddHours(-1), ReservationStatus.Seated);
        DateTime placed = DateTime.UtcNow.AddMinutes(-placedMinutesAgo);
        var order = new Order { ReservationId = visit.Id, CustomerId = customerId, Status = OrderStatus.Placed, TotalAmount = 20m,
            PlacedAtUtc = placed, UpdatedAtUtc = placed };
        order.Items.Add(new OrderItem { MenuItemId = dish.Id, Quantity = 1, UnitPrice = 20m });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }
}
