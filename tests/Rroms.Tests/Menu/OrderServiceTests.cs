using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
using Rroms.Web.Services;

namespace Rroms.Tests.Menu;

/// <summary>
/// ACCEPTANCE tests for placing orders (YJ-3), written BEFORE the code they test.
/// Owner: YJ, pair-programmed with YY.
/// </summary>
/// <remarks>
/// Every test follows Arrange (prepare data) - Act (call the method) - Assert (check the result), and gets a
/// brand-new in-memory SQLite database with WL's seed data (tables, categories) already in it.
/// The headline rules: the server reads prices and availability from the database (the request has no price),
/// a refused order saves nothing, and only the customer's own Confirmed or Seated reservation can be ordered for.
/// </remarks>
public class OrderServiceTests : IDisposable
{
    private const decimal BurgerPrice = 20.00m;
    private const decimal DrinkPrice = 12.50m;
    private const decimal OfferPrice = 15.00m;
    private const decimal ChangedPrice = 25.00m;

    /// <summary>A dish Id that no test creates.</summary>
    private const int UnknownDishId = 999999;

    /// <summary>A reservation Id that no test creates.</summary>
    private const int UnknownReservationId = 999999;

    /// <summary>Id of the seeded "Beverages" category.</summary>
    private const int BeveragesId = 5;

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly FakeNotificationService _notifications;
    private readonly OrderRulesOptions _rules;
    private readonly OrderService _service;

    /// <summary>Runs before EVERY test: creates a brand-new in-memory database and the service under test.</summary>
    public OrderServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _notifications = new FakeNotificationService();
        _rules = new OrderRulesOptions();
        _service = new OrderService(_context, _notifications, Microsoft.Extensions.Options.Options.Create(_rules));
    }

    /// <summary>Runs after EVERY test: closing the connection deletes the database.</summary>
    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<Reservation> AddReservationAsync(ReservationStatus status = ReservationStatus.Confirmed)
    {
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);

        return await OrderTestData.AddReservationAsync(_context, customer.Id, status);
    }

    private static PlaceOrderRequest BuildRequest(int reservationId, params PlaceOrderLine[] lines)
    {
        var request = new PlaceOrderRequest { ReservationId = reservationId };

        foreach (PlaceOrderLine line in lines)
        {
            request.Lines.Add(line);
        }

        return request;
    }

    private static PlaceOrderLine Line(int dishId, int quantity)
    {
        return new PlaceOrderLine { MenuItemId = dishId, Quantity = quantity };
    }

    private async Task<int> CountOrdersAsync()
    {
        return await _context.Orders.CountAsync();
    }

    //================================================== A valid order ==================================================//

    [Fact]
    public async Task PlaceOrder_ValidRequest_SavesTheOrderAndItsLinesAsPlaced()
    {
        // Arrange: 2 burgers and 1 drink.
        Reservation reservation = await AddReservationAsync();
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger", BurgerPrice);
        MenuItem drink = await OrderTestData.AddDishAsync(_context, "Lemon Tea", DrinkPrice, categoryId: BeveragesId);
        PlaceOrderRequest request = BuildRequest(reservation.Id, Line(burger.Id, 2), Line(drink.Id, 1));

        // Act
        ServiceResult<int> result = await _service.PlaceOrderAsync(reservation.CustomerId, request);

        // Assert: saved as Placed, for the reservation's customer, with the total added up by the server.
        Assert.True(result.Succeeded);
        Order saved = await _context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == result.Value);
        Assert.Equal(OrderStatus.Placed, saved.Status);
        Assert.Equal(reservation.Id, saved.ReservationId);
        Assert.Equal(reservation.CustomerId, saved.CustomerId);
        Assert.Equal(2 * BurgerPrice + DrinkPrice, saved.TotalAmount);
        Assert.Equal(2, saved.Items.Count);
    }

    [Fact]
    public async Task PlaceOrder_DishWithARunningOffer_IsChargedTheOfferPrice()
    {
        // Arrange: an offer that started yesterday and ends tomorrow.
        Reservation reservation = await AddReservationAsync();
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Offer Burger", BurgerPrice);
        burger.PromoPrice = OfferPrice;
        burger.PromoStartUtc = DateTime.UtcNow.AddDays(-1);
        burger.PromoEndUtc = DateTime.UtcNow.AddDays(1);
        await _context.SaveChangesAsync();

        // Act
        ServiceResult<int> result = await _service.PlaceOrderAsync(
            reservation.CustomerId, BuildRequest(reservation.Id, Line(burger.Id, 1)));

        // Assert
        Assert.True(result.Succeeded);
        OrderItem line = await _context.OrderItems.SingleAsync(i => i.OrderId == result.Value);
        Assert.Equal(OfferPrice, line.UnitPrice);
    }

    [Fact]
    public async Task PlaceOrder_PriceChangedSinceTheMenuWasShown_IsChargedTheCurrentPrice()
    {
        // Arrange: the customer saw RM 20, then the restaurant raised the price to RM 25 before they ordered.
        Reservation reservation = await AddReservationAsync();
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger", BurgerPrice);
        burger.Price = ChangedPrice;
        await _context.SaveChangesAsync();

        // Act
        ServiceResult<int> result = await _service.PlaceOrderAsync(
            reservation.CustomerId, BuildRequest(reservation.Id, Line(burger.Id, 2)));

        // Assert: the server used the price in the database, so the copy on the line and the total are RM 25-based.
        Assert.True(result.Succeeded);
        Order saved = await _context.Orders.Include(o => o.Items).SingleAsync(o => o.Id == result.Value);
        Assert.Equal(ChangedPrice, saved.Items.Single().UnitPrice);
        Assert.Equal(2 * ChangedPrice, saved.TotalAmount);
    }

    [Fact]
    public async Task PlaceOrder_SameDishTwice_IsMergedIntoOneLine()
    {
        // Arrange: 1 burger on one line and 2 burgers on another.
        Reservation reservation = await AddReservationAsync();
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger", BurgerPrice);

        // Act
        ServiceResult<int> result = await _service.PlaceOrderAsync(
            reservation.CustomerId, BuildRequest(reservation.Id, Line(burger.Id, 1), Line(burger.Id, 2)));

        // Assert
        Assert.True(result.Succeeded);
        OrderItem line = await _context.OrderItems.SingleAsync(i => i.OrderId == result.Value);
        Assert.Equal(3, line.Quantity);
    }

    //================================================== Dishes that cannot be ordered ==================================================//

    [Fact]
    public async Task PlaceOrder_UnavailableDish_RefusesTheWholeOrderAndSavesNothing()
    {
        // Arrange: one good dish and one switched off.
        Reservation reservation = await AddReservationAsync();
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger", BurgerPrice);
        MenuItem special = await OrderTestData.AddDishAsync(_context, "Sold Out Special", BurgerPrice, isAvailable: false);

        // Act
        ServiceResult<int> result = await _service.PlaceOrderAsync(
            reservation.CustomerId, BuildRequest(reservation.Id, Line(burger.Id, 1), Line(special.Id, 1)));

        // Assert: refused with the dish named, and no partial order was left behind.
        Assert.False(result.Succeeded);
        Assert.Contains(OrderService.DishUnavailableMessage("Sold Out Special"), result.Errors);
        Assert.Equal(0, await CountOrdersAsync());
    }

    [Fact]
    public async Task PlaceOrder_DishInAHiddenCategory_IsRefused()
    {
        // Arrange: switch Beverages off after adding a drink to it.
        Reservation reservation = await AddReservationAsync();
        MenuItem drink = await OrderTestData.AddDishAsync(_context, "Lemon Tea", DrinkPrice, categoryId: BeveragesId);
        MenuCategory beverages = await _context.MenuCategories.SingleAsync(c => c.Id == BeveragesId);
        beverages.IsActive = false;
        await _context.SaveChangesAsync();

        // Act
        ServiceResult<int> result = await _service.PlaceOrderAsync(
            reservation.CustomerId, BuildRequest(reservation.Id, Line(drink.Id, 1)));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(OrderService.DishUnavailableMessage("Lemon Tea"), result.Errors);
        Assert.Equal(0, await CountOrdersAsync());
    }

    [Fact]
    public async Task PlaceOrder_UnknownDish_IsRefused()
    {
        // Arrange
        Reservation reservation = await AddReservationAsync();

        // Act
        ServiceResult<int> result = await _service.PlaceOrderAsync(
            reservation.CustomerId, BuildRequest(reservation.Id, Line(UnknownDishId, 1)));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(OrderService.UnknownDishMessage, result.Errors);
        Assert.Equal(0, await CountOrdersAsync());
    }

    //================================================== The shape of the order ==================================================//

    [Fact]
    public async Task PlaceOrder_NoDishes_IsRefused()
    {
        // Arrange
        Reservation reservation = await AddReservationAsync();

        // Act
        ServiceResult<int> result = await _service.PlaceOrderAsync(
            reservation.CustomerId, BuildRequest(reservation.Id));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(OrderService.EmptyOrderMessage, result.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(21)]
    public async Task PlaceOrder_QuantityOutsideOneToTwenty_IsRefused(int quantity)
    {
        // Arrange: runs three times, with 0, -1 and 21 (the limit is 20).
        Reservation reservation = await AddReservationAsync();
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger", BurgerPrice);

        // Act
        ServiceResult<int> result = await _service.PlaceOrderAsync(
            reservation.CustomerId, BuildRequest(reservation.Id, Line(burger.Id, quantity)));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(OrderService.QuantityOutOfRangeMessage(_rules.MaxQuantityPerLine), result.Errors);
        Assert.Equal(0, await CountOrdersAsync());
    }

    [Fact]
    public async Task PlaceOrder_NoteLongerThanTheDatabaseAllows_IsRefused()
    {
        // Arrange
        Reservation reservation = await AddReservationAsync();
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger", BurgerPrice);
        PlaceOrderRequest request = BuildRequest(reservation.Id, Line(burger.Id, 1));
        request.Notes = new string('x', Order.NotesMaxLength + 1);

        // Act
        ServiceResult<int> result = await _service.PlaceOrderAsync(reservation.CustomerId, request);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(OrderService.NotesTooLongMessage, result.Errors);
    }

    //================================================== Whose reservation, and in what state ==================================================//

    [Fact]
    public async Task PlaceOrder_SomeoneElsesReservation_IsRefusedAsNotFound()
    {
        // Arrange: the reservation belongs to one customer, another customer tries to order on it.
        Reservation reservation = await AddReservationAsync();
        ApplicationUser stranger = await OrderTestData.AddCustomerAsync(_context);
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger", BurgerPrice);

        // Act
        ServiceResult<int> result = await _service.PlaceOrderAsync(
            stranger.Id, BuildRequest(reservation.Id, Line(burger.Id, 1)));

        // Assert: the same answer as for a reservation that does not exist, so nothing about it is revealed.
        Assert.False(result.Succeeded);
        Assert.Contains(OrderService.ReservationNotFoundMessage, result.Errors);
        Assert.Equal(0, await CountOrdersAsync());
    }

    [Fact]
    public async Task PlaceOrder_ReservationThatDoesNotExist_IsRefusedAsNotFound()
    {
        // Arrange
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger", BurgerPrice);

        // Act
        ServiceResult<int> result = await _service.PlaceOrderAsync(
            customer.Id, BuildRequest(UnknownReservationId, Line(burger.Id, 1)));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(OrderService.ReservationNotFoundMessage, result.Errors);
    }

    [Theory]
    [InlineData(ReservationStatus.Pending)]
    [InlineData(ReservationStatus.Completed)]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.NoShow)]
    public async Task PlaceOrder_ReservationNotConfirmedOrSeated_IsRefused(ReservationStatus status)
    {
        // Arrange: runs four times, once for every status that may not order.
        Reservation reservation = await AddReservationAsync(status);
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger", BurgerPrice);

        // Act
        ServiceResult<int> result = await _service.PlaceOrderAsync(
            reservation.CustomerId, BuildRequest(reservation.Id, Line(burger.Id, 1)));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(OrderService.ReservationNotOrderableMessage, result.Errors);
        Assert.Equal(0, await CountOrdersAsync());
    }

    [Theory]
    [InlineData(ReservationStatus.Confirmed)]
    [InlineData(ReservationStatus.Seated)]
    public async Task PlaceOrder_ConfirmedOrSeatedReservation_IsAccepted(ReservationStatus status)
    {
        // Arrange: pre-ordering (Confirmed) and ordering at the table (Seated) are both allowed.
        Reservation reservation = await AddReservationAsync(status);
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger", BurgerPrice);

        // Act
        ServiceResult<int> result = await _service.PlaceOrderAsync(
            reservation.CustomerId, BuildRequest(reservation.Id, Line(burger.Id, 1)));

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task GetOrderableReservation_OwnConfirmedReservation_ReturnsTheTable()
    {
        // Arrange
        Reservation reservation = await AddReservationAsync();

        // Act
        ServiceResult<OrderableReservation> result = await _service.GetOrderableReservationAsync(
            reservation.CustomerId, reservation.Id);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal(OrderTestData.SeededTableLabel, result.Value.TableLabel);
    }

    //================================================== Notifications ==================================================//

    [Fact]
    public async Task PlaceOrder_Success_NotifiesTheCustomerAndTheStaff()
    {
        // Arrange
        Reservation reservation = await AddReservationAsync();
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger", BurgerPrice);

        // Act
        ServiceResult<int> result = await _service.PlaceOrderAsync(
            reservation.CustomerId, BuildRequest(reservation.Id, Line(burger.Id, 1)));

        // Assert: the customer gets a confirmation linking to the order, and the staff role hears about it.
        Assert.True(result.Succeeded);

        FakeNotificationService.RecordedMessage toCustomer = Assert.Single(_notifications.ToUsers);
        Assert.Equal(reservation.CustomerId, toCustomer.Recipient);
        Assert.Equal(NotificationType.OrderUpdated, toCustomer.Type);
        Assert.Contains($"/Customer/Orders/Details/{result.Value}", toCustomer.LinkUrl);

        FakeNotificationService.RecordedMessage toStaff = Assert.Single(_notifications.ToRoles);
        Assert.Equal(RoleNames.Staff, toStaff.Recipient);
        Assert.Equal(NotificationType.OrderUpdated, toStaff.Type);
    }

    [Fact]
    public async Task PlaceOrder_Refused_SendsNoNotification()
    {
        // Arrange: an order that will be refused (empty).
        Reservation reservation = await AddReservationAsync();

        // Act
        await _service.PlaceOrderAsync(reservation.CustomerId, BuildRequest(reservation.Id));

        // Assert
        Assert.Empty(_notifications.ToUsers);
        Assert.Empty(_notifications.ToRoles);
    }

    //================================================== Reading an order back ==================================================//

    [Fact]
    public async Task GetOrderForCustomer_OwnOrder_ReturnsLinesAndTotal()
    {
        // Arrange
        Reservation reservation = await AddReservationAsync();
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger", BurgerPrice);
        ServiceResult<int> placed = await _service.PlaceOrderAsync(
            reservation.CustomerId, BuildRequest(reservation.Id, Line(burger.Id, 2)));

        // Act
        OrderSummary? summary = await _service.GetOrderForCustomerAsync(reservation.CustomerId, placed.Value);

        // Assert
        Assert.NotNull(summary);
        Assert.Equal(2 * BurgerPrice, summary.TotalAmount);
        Assert.Equal(OrderTestData.SeededTableLabel, summary.TableLabel);
        OrderSummaryLine line = Assert.Single(summary.Lines);
        Assert.Equal("Burger", line.DishName);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(2 * BurgerPrice, line.LineTotal);
    }

    [Fact]
    public async Task GetOrderForCustomer_AnotherCustomersOrder_ReturnsNull()
    {
        // Arrange
        Reservation reservation = await AddReservationAsync();
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger", BurgerPrice);
        ServiceResult<int> placed = await _service.PlaceOrderAsync(
            reservation.CustomerId, BuildRequest(reservation.Id, Line(burger.Id, 1)));
        ApplicationUser stranger = await OrderTestData.AddCustomerAsync(_context);

        // Act
        OrderSummary? summary = await _service.GetOrderForCustomerAsync(stranger.Id, placed.Value);

        // Assert: a stranger learns nothing, not even that the order exists.
        Assert.Null(summary);
    }
}
