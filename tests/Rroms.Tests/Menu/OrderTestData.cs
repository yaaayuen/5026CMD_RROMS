using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.Menu;

/// <summary>
/// Builds the rows an order needs (a customer, a reservation, dishes) so tests can set up a situation in one call.
/// Shared by the menu service tests, the order entity tests and the order tests. Owner: YJ.
/// </summary>
public static class OrderTestData
{
    /// <summary>Id of the seeded table "T01", which every test database contains.</summary>
    public const int SeededTableId = 1;

    /// <summary>Label of the seeded table "T01".</summary>
    public const string SeededTableLabel = "T01";

    /// <summary>Id of the seeded "Grills" category.</summary>
    public const int GrillsId = 2;

    private const int PartySize = 2;
    private const int VisitMinutes = 120;

    /// <summary>Saves a new customer account.</summary>
    /// <param name="context">The test database.</param>
    public static async Task<ApplicationUser> AddCustomerAsync(ApplicationDbContext context)
    {
        string email = $"customer-{Guid.NewGuid():N}@threegrills.test";

        var customer = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = "Test Customer"
        };

        context.Users.Add(customer);
        await context.SaveChangesAsync();

        return customer;
    }

    /// <summary>Saves a reservation at the seeded table for the given customer.</summary>
    /// <param name="context">The test database.</param>
    /// <param name="customerId">The customer who booked.</param>
    /// <param name="status">The reservation's status. Confirmed unless a test needs another one.</param>
    public static async Task<Reservation> AddReservationAsync(
        ApplicationDbContext context,
        string customerId,
        ReservationStatus status = ReservationStatus.Confirmed)
    {
        DateTime start = new DateTime(2026, 10, 20, 11, 0, 0, DateTimeKind.Utc);

        var reservation = new Reservation
        {
            CustomerId = customerId,
            DiningTableId = SeededTableId,
            PartySize = PartySize,
            StartUtc = start,
            EndUtc = start.AddMinutes(VisitMinutes),
            Status = status,
            CreatedAtUtc = start,
            UpdatedAtUtc = start
        };

        context.Set<Reservation>().Add(reservation);
        await context.SaveChangesAsync();

        return reservation;
    }

    /// <summary>Saves a dish straight into the database.</summary>
    /// <param name="context">The test database.</param>
    /// <param name="name">The dish name.</param>
    /// <param name="price">The normal price in RM.</param>
    /// <param name="isAvailable">False to switch ordering off for the dish.</param>
    /// <param name="categoryId">The menu category. Grills unless a test needs another one.</param>
    public static async Task<MenuItem> AddDishAsync(
        ApplicationDbContext context,
        string name,
        decimal price = 20.00m,
        bool isAvailable = true,
        int categoryId = GrillsId)
    {
        DateTime now = DateTime.UtcNow;

        var dish = new MenuItem
        {
            Name = name,
            Description = "Made for a test",
            MenuCategoryId = categoryId,
            Price = price,
            IsAvailable = isAvailable,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        context.MenuItems.Add(dish);
        await context.SaveChangesAsync();

        return dish;
    }

    /// <summary>
    /// Saves a new customer, a Confirmed reservation for them, and an order with one line for the given dish.
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
        ApplicationUser customer = await AddCustomerAsync(context);
        Reservation reservation = await AddReservationAsync(context, customer.Id);
        DateTime start = reservation.StartUtc;

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
