using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Tests.Menu;

/// <summary>
/// ACCEPTANCE tests for the customer ordering pages (YJ-3), written BEFORE the pages they test.
/// Owner: YJ, pair-programmed with YY.
/// </summary>
/// <remarks>
/// Integration tests in the same style as MenuPagesTests: the whole app starts in memory, tests sign in
/// through the real login form and send real HTTP requests. All tests in this class share one database,
/// so every dish gets a unique name.
/// </remarks>
public class OrderPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";

    private const string NewOrderUrl = "/Customer/Orders/New";
    private const string DetailsUrl = "/Customer/Orders/Details";

    private const decimal BurgerPrice = 20.00m;
    private const string TamperedPrice = "0.01";

    private const int MissingReservationId = 999999;
    private const int MissingOrderId = 999999;

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public OrderPagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ============================== Access control ==============================//

    /// <summary>Someone who is not signed in is sent to the login page.</summary>
    [Theory]
    [InlineData($"{NewOrderUrl}/1")]
    [InlineData($"{DetailsUrl}/1")]
    public async Task Anonymous_IsSentToLogin(string url)
    {
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    /// <summary>Staff and administrators are signed in but are not customers, so they are refused.</summary>
    [Theory]
    [InlineData(RoleNames.Staff)]
    [InlineData(RoleNames.Administrator)]
    public async Task NonCustomer_IsDenied(string roleName)
    {
        SignedInUser user = await SignInNewUserAsync(roleName);

        HttpResponseMessage response = await user.Client.GetAsync($"{NewOrderUrl}/1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    // ============================== The order page ==============================//

    /// <summary>A customer sees the menu and their table on the order page of their own Confirmed reservation.</summary>
    [Fact]
    public async Task OrderPage_OwnConfirmedReservation_ShowsTheMenuAndTheTable()
    {
        SignedInUser customer = await SignInNewUserAsync(RoleNames.Customer);
        Reservation reservation = await AddReservationAsync(customer.Id);
        string dishName = NewDishName();
        await AddDishAsync(dishName);

        HttpResponseMessage response = await customer.Client.GetAsync($"{NewOrderUrl}/{reservation.Id}");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(dishName, html);
        Assert.Contains(OrderTestData.SeededTableLabel, html);
    }

    /// <summary>Another customer's reservation looks exactly like one that does not exist: 404.</summary>
    [Fact]
    public async Task OrderPage_SomeoneElsesReservation_ReturnsNotFound()
    {
        SignedInUser owner = await SignInNewUserAsync(RoleNames.Customer);
        SignedInUser stranger = await SignInNewUserAsync(RoleNames.Customer);
        Reservation reservation = await AddReservationAsync(owner.Id);

        HttpResponseMessage response = await stranger.Client.GetAsync($"{NewOrderUrl}/{reservation.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A reservation that does not exist returns 404.</summary>
    [Fact]
    public async Task OrderPage_UnknownReservation_ReturnsNotFound()
    {
        SignedInUser customer = await SignInNewUserAsync(RoleNames.Customer);

        HttpResponseMessage response = await customer.Client.GetAsync($"{NewOrderUrl}/{MissingReservationId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A reservation that is still Pending shows an explanation instead of the order form.</summary>
    [Fact]
    public async Task OrderPage_PendingReservation_ExplainsWhyItCannotBeOrderedYet()
    {
        SignedInUser customer = await SignInNewUserAsync(RoleNames.Customer);
        Reservation reservation = await AddReservationAsync(customer.Id, ReservationStatus.Pending);

        HttpResponseMessage response = await customer.Client.GetAsync($"{NewOrderUrl}/{reservation.Id}");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(OrderService.ReservationNotOrderableMessage, html);
    }

    // ============================== Placing an order ==============================//

    /// <summary>
    /// A valid form saves the order and redirects to the confirmation page. The browser also sends a made-up
    /// price, which must be ignored: the order is charged the price in the database.
    /// </summary>
    [Fact]
    public async Task PlaceOrder_ValidForm_SavesAtTheServerPriceAndIgnoresATamperedPrice()
    {
        SignedInUser customer = await SignInNewUserAsync(RoleNames.Customer);
        Reservation reservation = await AddReservationAsync(customer.Id);
        MenuItem dish = await AddDishAsync(NewDishName());
        string url = $"{NewOrderUrl}/{reservation.Id}";
        var fields = new Dictionary<string, string>
        {
            ["Lines[0].MenuItemId"] = dish.Id.ToString(),
            ["Lines[0].Quantity"] = "2",
            ["Lines[0].UnitPrice"] = TamperedPrice,
            ["Notes"] = "No onions please"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(customer.Client, url, url, fields);
        Order? saved = await FindOrderAsync(reservation.Id);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"{DetailsUrl}/", response.Headers.Location?.ToString());
        Assert.NotNull(saved);
        Assert.Equal(46.64m, saved.TotalAmount);
        Assert.Equal(BurgerPrice, saved.Items.Single().UnitPrice);
    }

    /// <summary>A dish that cannot be ordered is refused with the service's message, and nothing is saved.</summary>
    [Fact]
    public async Task PlaceOrder_UnavailableDish_ShowsTheMessageAndSavesNothing()
    {
        SignedInUser customer = await SignInNewUserAsync(RoleNames.Customer);
        Reservation reservation = await AddReservationAsync(customer.Id);
        string dishName = NewDishName();
        MenuItem dish = await AddDishAsync(dishName, isAvailable: false);
        string url = $"{NewOrderUrl}/{reservation.Id}";
        var fields = new Dictionary<string, string>
        {
            ["Lines[0].MenuItemId"] = dish.Id.ToString(),
            ["Lines[0].Quantity"] = "1"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(customer.Client, url, url, fields);
        string html = await response.Content.ReadAsStringAsync();
        Order? saved = await FindOrderAsync(reservation.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(OrderService.DishUnavailableMessage(dishName), html);
        Assert.Null(saved);
    }

    /// <summary>The form lists every dish with quantity 0 by default; sending only zeros is an empty order.</summary>
    [Fact]
    public async Task PlaceOrder_AllQuantitiesZero_ShowsTheEmptyOrderMessage()
    {
        SignedInUser customer = await SignInNewUserAsync(RoleNames.Customer);
        Reservation reservation = await AddReservationAsync(customer.Id);
        MenuItem dish = await AddDishAsync(NewDishName());
        string url = $"{NewOrderUrl}/{reservation.Id}";
        var fields = new Dictionary<string, string>
        {
            ["Lines[0].MenuItemId"] = dish.Id.ToString(),
            ["Lines[0].Quantity"] = "0"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(customer.Client, url, url, fields);
        string html = await response.Content.ReadAsStringAsync();
        Order? saved = await FindOrderAsync(reservation.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(OrderService.EmptyOrderMessage, html);
        Assert.Null(saved);
    }

    // ============================== The confirmation page ==============================//

    /// <summary>The confirmation page of the customer's own order shows the dish and the total.</summary>
    [Fact]
    public async Task Details_OwnOrder_ShowsTheDishAndTheTotal()
    {
        SignedInUser customer = await SignInNewUserAsync(RoleNames.Customer);
        Reservation reservation = await AddReservationAsync(customer.Id);
        string dishName = NewDishName();
        MenuItem dish = await AddDishAsync(dishName);
        int orderId = await PlaceOrderThroughTheServiceAsync(customer.Id, reservation.Id, dish.Id, 2);

        HttpResponseMessage response = await customer.Client.GetAsync($"{DetailsUrl}/{orderId}");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(dishName, html);
        Assert.Contains("40.00", html);
    }

    /// <summary>Another customer's order returns 404, as if it did not exist.</summary>
    [Fact]
    public async Task Details_AnotherCustomersOrder_ReturnsNotFound()
    {
        SignedInUser owner = await SignInNewUserAsync(RoleNames.Customer);
        SignedInUser stranger = await SignInNewUserAsync(RoleNames.Customer);
        Reservation reservation = await AddReservationAsync(owner.Id);
        MenuItem dish = await AddDishAsync(NewDishName());
        int orderId = await PlaceOrderThroughTheServiceAsync(owner.Id, reservation.Id, dish.Id, 1);

        HttpResponseMessage response = await stranger.Client.GetAsync($"{DetailsUrl}/{orderId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>An order that does not exist returns 404.</summary>
    [Fact]
    public async Task Details_MissingOrder_ReturnsNotFound()
    {
        SignedInUser customer = await SignInNewUserAsync(RoleNames.Customer);

        HttpResponseMessage response = await customer.Client.GetAsync($"{DetailsUrl}/{MissingOrderId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ============================== Helpers ==============================//

    /// <summary>A signed-in test user: the browser stand-in and the user's ID.</summary>
    /// <param name="Client">An HTTP client that is signed in as the user.</param>
    /// <param name="Id">The user's Identity ID.</param>
    private record SignedInUser(HttpClient Client, string Id);

    /// <summary>Creates a new user with the given role and signs in as them through the real login form.</summary>
    /// <param name="roleName">For example <see cref="RoleNames.Customer"/>.</param>
    private async Task<SignedInUser> SignInNewUserAsync(string roleName)
    {
        string email = $"{roleName.ToLowerInvariant()}-{Guid.NewGuid():N}@threegrills.test";
        ApplicationUser user = await _factory.CreateUserAsync(email, Password, roleName);

        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);

        return new SignedInUser(client, user.Id);
    }

    /// <summary>A name no other test uses, so tests that share the database never clash.</summary>
    private static string NewDishName()
    {
        return "Order dish " + Guid.NewGuid().ToString("N");
    }

    private async Task<Reservation> AddReservationAsync(string customerId, ReservationStatus status = ReservationStatus.Confirmed)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await OrderTestData.AddReservationAsync(context, customerId, status);
    }

    private async Task<MenuItem> AddDishAsync(string name, bool isAvailable = true)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await OrderTestData.AddDishAsync(context, name, BurgerPrice, isAvailable);
    }

    /// <summary>Reads the order saved for a reservation (with its lines), or null when none was saved.</summary>
    private async Task<Order?> FindOrderAsync(int reservationId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.Orders
            .Include(order => order.Items)
            .SingleOrDefaultAsync(order => order.ReservationId == reservationId);
    }

    /// <summary>Places an order straight through the service, to set up a confirmation page test.</summary>
    private async Task<int> PlaceOrderThroughTheServiceAsync(string customerId, int reservationId, int dishId, int quantity)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IOrderService service = scope.ServiceProvider.GetRequiredService<IOrderService>();

        var request = new PlaceOrderRequest { ReservationId = reservationId };
        request.Lines.Add(new PlaceOrderLine { MenuItemId = dishId, Quantity = quantity });

        ServiceResult<int> result = await service.PlaceOrderAsync(customerId, request);
        Assert.True(result.Succeeded);

        return result.Value;
    }
}
