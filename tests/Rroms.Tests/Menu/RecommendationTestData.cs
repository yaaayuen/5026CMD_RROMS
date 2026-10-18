using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.Menu;

/// <summary>
/// Builds orders and tagged dishes for the recommendation tests. Works on top of <see cref="OrderTestData"/>
/// and changes nothing in it. Owner: YJ.
/// </summary>
public static class RecommendationTestData
{
    /// <summary>The price paid per portion in the orders built here.</summary>
    private const decimal UnitPrice = 20.00m;

    /// <summary>Saves an order placed now (status Placed) for an existing customer.</summary>
    /// <param name="context">The test database.</param>
    /// <param name="customerId">The customer who orders.</param>
    /// <param name="lines">The dishes and quantities ordered.</param>
    public static async Task<Order> AddOrderAsync(ApplicationDbContext context, string customerId, params OrderTestLine[] lines)
    {
        return await AddOrderAsync(context, customerId, DateTime.UtcNow, OrderStatus.Placed, lines);
    }

    /// <summary>Saves an order for an existing customer with a chosen time and status.</summary>
    /// <param name="context">The test database.</param>
    /// <param name="customerId">The customer who orders.</param>
    /// <param name="placedAtUtc">When the order was placed, for example 40 days ago.</param>
    /// <param name="status">The order's status, for example Cancelled.</param>
    /// <param name="lines">The dishes and quantities ordered.</param>
    public static async Task<Order> AddOrderAsync(
        ApplicationDbContext context,
        string customerId,
        DateTime placedAtUtc,
        OrderStatus status,
        params OrderTestLine[] lines)
    {
        Reservation reservation = await OrderTestData.AddReservationAsync(context, customerId);

        var order = new Order
        {
            ReservationId = reservation.Id,
            CustomerId = customerId,
            Status = status,
            PlacedAtUtc = placedAtUtc,
            UpdatedAtUtc = placedAtUtc
        };

        decimal total = 0m;

        foreach (OrderTestLine line in lines)
        {
            order.Items.Add(new OrderItem
            {
                MenuItemId = line.DishId,
                Quantity = line.Quantity,
                UnitPrice = UnitPrice
            });

            total += UnitPrice * line.Quantity;
        }

        order.TotalAmount = total;

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return order;
    }

    /// <summary>Marks a dish as containing the given dietary tag (1 animal products, 2 dairy, 3 spicy).</summary>
    /// <param name="context">The test database.</param>
    /// <param name="dishId">The dish to tag.</param>
    /// <param name="tagId">The tag the dish contains.</param>
    public static async Task TagDishAsync(ApplicationDbContext context, int dishId, int tagId)
    {
        MenuItem dish = await context.MenuItems.FindAsync(dishId) ?? throw new InvalidOperationException("Dish not found.");
        DietaryTag tag = await context.DietaryTags.FindAsync(tagId) ?? throw new InvalidOperationException("Tag not found.");

        context.Entry(dish).Collection(item => item.DietaryTags).Load();
        dish.DietaryTags.Add(tag);

        await context.SaveChangesAsync();
    }
}
