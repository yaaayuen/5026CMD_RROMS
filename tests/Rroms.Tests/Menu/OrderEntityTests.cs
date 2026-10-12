using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.Menu;

/// <summary>
/// Tests for the YJ-0 order tables: what saves, what the database refuses, 
/// and what is deleted with what.
/// </summary>
/// <remarks>
/// These tests talk to the database directly (no service yet; 
/// the order service arrives in YJ-3).
/// Every test follows Arrange (prepare data) - Act (do the thing) 
/// - Assert (check the result), and gets a brand-new in-memory SQLite database, 
/// with WL's seed data (tables, categories) already in it.
/// </remarks>
public class OrderEntityTests : IDisposable
{
    /// <summary>Id of the seeded "Grills" category.</summary>
    private const int GrillsId = 2;

    /// <summary>A reservation Id that no test creates.</summary>
    private const int MissingReservationId = 999999;

    private const decimal DishPrice = 20.00m;
    private const int Quantity = 2;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly ApplicationDbContext _context;

    /// <summary>Runs before EVERY test: creates a brand-new in-memory database.</summary>
    public OrderEntityTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(_options);
        _context.Database.EnsureCreated();
    }

    /// <summary>Runs after EVERY test: closing the connection deletes the database.</summary>
    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>Saves a dish straight into the database 
    /// so orders have something to refer to.</summary>
    private async Task<MenuItem> AddDishAsync()
    {
        DateTime now = DateTime.UtcNow;

        var dish = new MenuItem
        {
            Name = "Test dish",
            Description = "Made for a test",
            MenuCategoryId = GrillsId,
            Price = DishPrice,
            IsAvailable = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _context.MenuItems.Add(dish);
        await _context.SaveChangesAsync();

        return dish;
    }

    //================================================== Saving and reading ==================================================//

    [Fact]
    public async Task Order_WithALine_ReadsBackAsPlacedWithItsLine()
    {
        // Arrange
        MenuItem dish = await AddDishAsync();
        Order saved = await OrderTestData.AddOrderAsync(_context, dish.Id, Quantity, DishPrice);

        // Act: read it back with a second context, so nothing comes from the first one's memory.
        using var other = new ApplicationDbContext(_options);
        Order loaded = await other.Orders.Include(o => o.Items).SingleAsync(o => o.Id == saved.Id);

        // Assert: a new order starts as Placed, and its line and total are intact.
        Assert.Equal(OrderStatus.Placed, loaded.Status);
        Assert.Equal(Quantity * DishPrice, loaded.TotalAmount);
        Assert.Single(loaded.Items);
        Assert.Equal(DishPrice, loaded.Items.First().UnitPrice);
    }

    [Fact]
    public void OrderItem_LineTotal_IsQuantityTimesUnitPrice()
    {
        // Arrange
        var line = new OrderItem { Quantity = 3, UnitPrice = 12.50m };

        // Act
        decimal total = line.LineTotal;

        // Assert
        Assert.Equal(37.50m, total);
    }

    //================================================== What is deleted with what ==================================================//

    [Fact]
    public async Task DeleteOrder_AlsoDeletesItsLines()
    {
        // Arrange
        MenuItem dish = await AddDishAsync();
        Order saved = await OrderTestData.AddOrderAsync(_context, dish.Id);

        // Act: delete in the database itself (not through the change tracker), so only the cascade rule can remove the lines.
        using var other = new ApplicationDbContext(_options);
        await other.Orders.Where(o => o.Id == saved.Id).ExecuteDeleteAsync();

        // Assert
        int remainingLines = await other.OrderItems.CountAsync(i => i.OrderId == saved.Id);
        Assert.Equal(0, remainingLines);
    }

    [Fact]
    public async Task DeleteDishThatWasOrdered_IsBlockedByTheDatabase()
    {
        // Arrange
        MenuItem dish = await AddDishAsync();
        await OrderTestData.AddOrderAsync(_context, dish.Id);

        // Act + Assert: the foreign key refuses, so order history never loses a dish.
        using var other = new ApplicationDbContext(_options);
        MenuItem? tracked = await other.MenuItems.FindAsync(dish.Id);
        Assert.NotNull(tracked);
        other.MenuItems.Remove(tracked);

        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    //================================================== What the database refuses ==================================================//

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task OrderLine_WithQuantityZeroOrBelow_IsRefusedByTheDatabase(int quantity)
    {
        // Arrange
        MenuItem dish = await AddDishAsync();

        // Act + Assert: runs twice, with 0 and with -1.
        await Assert.ThrowsAsync<DbUpdateException>(
            () => OrderTestData.AddOrderAsync(_context, dish.Id, quantity, DishPrice));
    }

    [Fact]
    public async Task OrderLine_WithNegativePrice_IsRefusedByTheDatabase()
    {
        // Arrange
        MenuItem dish = await AddDishAsync();

        // Act + Assert
        await Assert.ThrowsAsync<DbUpdateException>(
            () => OrderTestData.AddOrderAsync(_context, dish.Id, 1, -5.00m));
    }

    [Fact]
    public async Task Order_ForAReservationThatDoesNotExist_IsRefusedByTheDatabase()
    {
        // Arrange: a real customer and dish, but a reservation Id that does not exist.
        MenuItem dish = await AddDishAsync();
        Order existing = await OrderTestData.AddOrderAsync(_context, dish.Id);

        var order = new Order
        {
            ReservationId = MissingReservationId,
            CustomerId = existing.CustomerId,
            TotalAmount = DishPrice,
            PlacedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        order.Items.Add(new OrderItem { MenuItemId = dish.Id, Quantity = 1, UnitPrice = DishPrice });
        _context.Orders.Add(order);

        // Act + Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }
}