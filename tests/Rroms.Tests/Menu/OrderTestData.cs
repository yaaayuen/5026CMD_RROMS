using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.Menu;

/// <summary>
/// Builds the rows an order needs (a customer and a reservation) 
/// so tests can create an order in one call.
/// Shared by the menu service tests and the order entity tests. Owner: YJ.
/// </summary>
public static class OrderTestData
{
    /// <summary>Id of the seeded table "T01", which every test database contains.</summary>
    public const int SeededTableId = 1;

    private const int PartySize = 2;
    private const int VisitMinutes = 120;

    /// <summary>
    /// Saves a new customer, a Confirmed reservation for them, 
    /// and an order with one line for the given dish.
    /// </summary>
    /// <param name="context">The test database.</param>
    /// <param name="menuItemId">The dish to order.</param>
    /// <param name="quantity">How many of the dish.</param>
    /// <param name="unitPrice">The price paid for one dish.</param>
    /// <returns>The saved order, including its line.</returns>
    public static async Task<Order> AddOrderAsync(
        ApplicationDbContext context,
        int menuItemId,
        int quantity = 2,
        decimal unitPrice = 20.00m)
    {
        string email = $"customer-{Guid.NewGuid():N}@threegrills.test";

        var customer = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = "Test Customer"
        };
        context.Users.Add(customer);

        DateTime start = new DateTime(2026, 10, 20, 11, 0, 0, DateTimeKind.Utc);

        var reservation = new Reservation
        {
            CustomerId = customer.Id,
            DiningTableId = SeededTableId,
            PartySize = PartySize,
            StartUtc = start,
            EndUtc = start.AddMinutes(VisitMinutes),
            Status = ReservationStatus.Confirmed,
            CreatedAtUtc = start,
            UpdatedAtUtc = start
        };
        context.Set<Reservation>().Add(reservation);
        await context.SaveChangesAsync();

        var order = new Order
        {
            ReservationId = reservation.Id,
            CustomerId = customer.Id,
            TotalAmount = quantity * unitPrice,
            PlacedAtUtc = start,
            UpdatedAtUtc = start
        };
        order.Items.Add(new OrderItem
        {
            MenuItemId = menuItemId,
            Quantity = quantity,
            UnitPrice = unitPrice
        });

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return order;
    }
}