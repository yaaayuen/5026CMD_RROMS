using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Common;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.Menu;

/// <summary>
/// YJ-4 acceptance for the three suggestion strips on the pages: "Popular this month" on the public menu,
/// "Recommended for you" on the order page and "People who ordered these also ordered" on the order
/// confirmation. Owner: YJ.
/// </summary>
/// <remarks>
/// Integration tests in the same style as OrderPagesTests. All tests in this class share one database, so every
/// dish has a unique name, and the popular dishes are ordered in very large numbers so they always rank on top.
/// </remarks>
public class RecommendationPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";
    private const string MenuUrl = "/Menu";
    private const string NewOrderUrl = "/Customer/Orders/New";
    private const string DetailsUrl = "/Customer/Orders/Details";

    private const decimal DishPrice = 20.00m;

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public RecommendationPagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>A visitor (not signed in) sees the popular strip with the dish and the reason.</summary>
    [Fact]
    public async Task Menu_Visitor_SeesThePopularStrip()
    {
        SignedInUser buyer = await CreateCustomerAsync();
        MenuItem dish = await AddDishAsync(NewDishName());
        await AddOrderAsync(buyer.Id, dish.Id, 800);
        HttpClient visitor = _factory.CreateSecureClient();

        string html = await visitor.GetStringAsync(MenuUrl);

        Assert.Contains("Popular this month", html);
        Assert.Contains(dish.Name, html);
        Assert.Contains("Ordered 800 times in the last 30 days", html);
    }

    /// <summary>When a filter is in use the strip is not shown, so it does not get in the way of the search.</summary>
    [Fact]
    public async Task Menu_WithAFilter_DoesNotShowThePopularStrip()
    {
        HttpClient visitor = _factory.CreateSecureClient();

        string html = await visitor.GetStringAsync($"{MenuUrl}?q=anything");

        Assert.DoesNotContain("Popular this month", html);
    }

    /// <summary>A customer with no order history gets the popular dishes under "Recommended for you".</summary>
    [Fact]
    public async Task OrderPage_CustomerWithoutHistory_SeesPopularDishesAsRecommended()
    {
        SignedInUser buyer = await CreateCustomerAsync();
        MenuItem dish = await AddDishAsync(NewDishName());
        await AddOrderAsync(buyer.Id, dish.Id, 900);
        SignedInUser newcomer = await CreateCustomerAsync();
        Reservation reservation = await AddReservationAsync(newcomer.Id);

        string html = await newcomer.Client.GetStringAsync($"{NewOrderUrl}/{reservation.Id}");

        Assert.Contains("Recommended for you", html);
        Assert.Contains("Ordered 900 times in the last 30 days", html);
    }

    /// <summary>The confirmation page suggests a dish other customers ordered together with the customer's dish.</summary>
    [Fact]
    public async Task OrderDetails_ShowsDishesOrderedTogether()
    {
        SignedInUser other = await CreateCustomerAsync();
        MenuItem main = await AddDishAsync(NewDishName());
        MenuItem side = await AddDishAsync(NewDishName());
        await RecommendationTestDataOrderAsync(other.Id, new OrderTestLine(main.Id, 1), new OrderTestLine(side.Id, 1));

        SignedInUser customer = await CreateCustomerAsync();
        Reservation reservation = await AddReservationAsync(customer.Id);
        int orderId = await PlaceOrderThroughTheServiceAsync(customer.Id, reservation.Id, main.Id);

        string html = await customer.Client.GetStringAsync($"{DetailsUrl}/{orderId}");

        Assert.Contains("People who ordered these also ordered", html);
        Assert.Contains(side.Name, html);
        Assert.Contains("Ordered together in 1 order", html);
    }

    // ============================== Helpers ==============================//

    private record SignedInUser(HttpClient Client, string Id);

    private async Task<SignedInUser> CreateCustomerAsync()
    {
        string email = $"customer-{Guid.NewGuid():N}@threegrills.test";
        ApplicationUser user = await _factory.CreateUserAsync(email, Password, RoleNames.Customer);

        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);

        return new SignedInUser(client, user.Id);
    }

    private static string NewDishName()
    {
        return "Suggest dish " + Guid.NewGuid().ToString("N");
    }

    private async Task<MenuItem> AddDishAsync(string name)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await OrderTestData.AddDishAsync(context, name, DishPrice, true);
    }

    private async Task<Reservation> AddReservationAsync(string customerId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await OrderTestData.AddReservationAsync(context, customerId, ReservationStatus.Confirmed);
    }

    private async Task AddOrderAsync(string customerId, int dishId, int quantity)
    {
        await RecommendationTestDataOrderAsync(customerId, new OrderTestLine(dishId, quantity));
    }

    private async Task RecommendationTestDataOrderAsync(string customerId, params OrderTestLine[] lines)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await RecommendationTestData.AddOrderAsync(context, customerId, lines);
    }

    private async Task<int> PlaceOrderThroughTheServiceAsync(string customerId, int reservationId, int dishId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        Rroms.Web.Services.IOrderService service = scope.ServiceProvider.GetRequiredService<Rroms.Web.Services.IOrderService>();

        var request = new Rroms.Web.Models.ViewModels.PlaceOrderRequest { ReservationId = reservationId };
        request.Lines.Add(new Rroms.Web.Models.ViewModels.PlaceOrderLine { MenuItemId = dishId, Quantity = 1 });

        ServiceResult<int> result = await service.PlaceOrderAsync(customerId, request);
        Assert.True(result.Succeeded);

        return result.Value;
    }
}