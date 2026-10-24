using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.Menu;

/// <summary>
/// ACCEPTANCE tests for the redesigned order details page (status, dishes with code and notes, bill, payment).
/// Written BEFORE the new page. Owner: YJ.
/// </summary>
/// <remarks>
/// The redesign only restyles the page. The bill breakdown, the payment card and the "ordered together" suggestions
/// belong to other work and must keep showing, so some tests below pass before the redesign (they guard against breaking it).
/// </remarks>
public class OrderDetailsDesignTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";
    private const string NewOrderUrl = "/Customer/Orders/New";
    private const decimal DishPrice = 20.00m;

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public OrderDetailsDesignTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The page shows the order status and a link to the live tracking page.</summary>
    [Fact]
    public async Task Details_ShowsTheStatus_AndALinkToLiveTracking()
    {
        (HttpClient client, string html, _) = await PlaceOrderAndOpenDetailsAsync(quantity: 1);

        Assert.Contains("Placed", html);
        Assert.Contains("Track live", html);
        Assert.Contains("href=\"/Customer/Tracking\"", html);
    }

    /// <summary>Each dish shows its code, and the note the customer wrote for it.</summary>
    [Fact]
    public async Task Details_ShowsTheDishCode_AndTheDishNote()
    {
        (_, string html, MenuItem dish) = await PlaceOrderAndOpenDetailsAsync(quantity: 1, dishNote: "less ice please");

        Assert.Contains(dish.Name, html);
        Assert.Contains(dish.Code!, html);
        Assert.Contains("less ice please", html);
    }

    /// <summary>The quantity and the line total are shown (2 x RM 20.00 = RM 40.00).</summary>
    [Fact]
    public async Task Details_ShowsTheLineTotal()
    {
        (_, string html, _) = await PlaceOrderAndOpenDetailsAsync(quantity: 2);

        Assert.Contains("40.00", html);
    }

    /// <summary>The customer's note for the kitchen is still shown.</summary>
    [Fact]
    public async Task Details_ShowsTheNoteForTheKitchen()
    {
        (_, string html, _) = await PlaceOrderAndOpenDetailsAsync(quantity: 1, orderNote: "allergic to peanuts");

        Assert.Contains("Note for the kitchen", html);
        Assert.Contains("allergic to peanuts", html);
    }

    /// <summary>Guard: the bill breakdown and the payment card (other work) must keep showing.</summary>
    [Fact]
    public async Task Details_StillShowsTheBillBreakdown_AndThePaymentCard()
    {
        (_, string html, _) = await PlaceOrderAndOpenDetailsAsync(quantity: 1);

        Assert.Contains("Subtotal", html);
        Assert.Contains("Service charge", html);
        Assert.Contains("Service tax", html);
        Assert.Contains("Payment", html);
        Assert.Contains("Unpaid", html);
    }

    /// <summary>Guard: a placed order still explains that it is prepared after the customer is seated.</summary>
    [Fact]
    public async Task Details_PlacedOrder_ExplainsWhenItWillBePrepared()
    {
        (_, string html, _) = await PlaceOrderAndOpenDetailsAsync(quantity: 1);

        Assert.Contains("prepared once you are seated", html);
    }

    /// <summary>Guard: the links back to the booking and to the history still work.</summary>
    [Fact]
    public async Task Details_StillLinksBackToTheBookingAndTheHistory()
    {
        (_, string html, _) = await PlaceOrderAndOpenDetailsAsync(quantity: 1);

        Assert.Contains("Back to my booking", html);
        Assert.Contains("My history", html);
    }

    // ============================== Helpers ==============================//

    /// <summary>Signs in a new customer, places an order through the order page, and opens its details page.</summary>
    private async Task<(HttpClient Client, string Html, MenuItem Dish)> PlaceOrderAndOpenDetailsAsync(
        int quantity, string? dishNote = null, string? orderNote = null)
    {
        string email = $"customer-{Guid.NewGuid():N}@threegrills.test";
        ApplicationUser user = await _factory.CreateUserAsync(email, Password, RoleNames.Customer);
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);

        MenuItem dish;
        Reservation reservation;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            reservation = await OrderTestData.AddReservationAsync(context, user.Id, ReservationStatus.Confirmed);
            dish = await OrderTestData.AddDishAsync(context, "Details dish " + Guid.NewGuid().ToString("N"), DishPrice, true);
            dish.Code = "T-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            await context.SaveChangesAsync();
        }

        string url = $"{NewOrderUrl}/{reservation.Id}";
        var fields = new Dictionary<string, string>
        {
            ["Lines[0].MenuItemId"] = dish.Id.ToString(),
            ["Lines[0].Quantity"] = quantity.ToString()
        };
        if (dishNote != null)
        {
            fields["Lines[0].Notes"] = dishNote;
        }
        if (orderNote != null)
        {
            fields["Notes"] = orderNote;
        }

        HttpResponseMessage response = await FormHelper.PostFormAsync(client, url, url, fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        string html = await client.GetStringAsync(response.Headers.Location!.ToString());

        return (client, html, dish);
    }
}
