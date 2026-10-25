using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.Menu;

/// <summary>
/// ACCEPTANCE tests for ordering from the Menu page (opt-in "Start ordering" view). Written BEFORE the page change. Owner: YJ.
/// </summary>
/// <remarks>
/// Contract: GET /Menu is always the plain menu. GET /Menu?order=1 turns on the ordering view for a signed-in customer
/// who has a reservation that can take an order (the order page's rule: own, Confirmed or Seated);
/// &amp;reservationId=N picks the reservation. POST /Menu?handler=PlaceOrder places an additional order on that reservation.
/// The server must ignore a reservation that is not the signed-in customer's.
/// </remarks>
public class MenuOrderingPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";
    private const string MenuUrl = "/Menu";
    private const string NewOrderUrl = "/Customer/Orders/New";
    private const decimal DishPrice = 20.00m;

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public MenuOrderingPagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>A visitor who is not signed in sees the plain menu and a "Sign in to order" link, never the ordering view.</summary>
    [Fact]
    public async Task Menu_Visitor_SeesASignInLink_AndNoOrdering()
    {
        HttpClient visitor = _factory.CreateSecureClient();

        string html = await visitor.GetStringAsync(MenuUrl);

        Assert.Contains("Sign in to order", html);
        Assert.DoesNotContain("Start ordering", html);
        Assert.DoesNotContain("id=\"orderBar\"", html);
    }

    /// <summary>?order=1 means nothing to a visitor: the page stays the plain menu.</summary>
    [Fact]
    public async Task Menu_VisitorAsksForTheOrderingView_StillGetsThePlainMenu()
    {
        HttpClient visitor = _factory.CreateSecureClient();

        string html = await visitor.GetStringAsync($"{MenuUrl}?order=1");

        Assert.DoesNotContain("id=\"orderBar\"", html);
        Assert.DoesNotContain("Ordering for", html);
    }

    /// <summary>A customer with no reservation that can take an order is pointed to Book a table.</summary>
    [Fact]
    public async Task Menu_CustomerWithoutAnOrderableReservation_IsToldToBookATable()
    {
        SignedInUser customer = await SignInNewCustomerAsync();

        string html = await customer.Client.GetStringAsync(MenuUrl);

        Assert.Contains("Book a table to order", html);
        Assert.DoesNotContain("Start ordering", html);
    }

    /// <summary>A customer with a reservation sees the plain menu first, with a Start ordering button and no steppers.</summary>
    [Fact]
    public async Task Menu_CustomerWithAReservation_StartsInBrowsingMode()
    {
        SignedInUser customer = await SignInNewCustomerAsync();
        await AddReservationAsync(customer.Id);
        await AddDishAsync();

        string html = await customer.Client.GetStringAsync(MenuUrl);

        Assert.Contains("Start ordering", html);
        Assert.DoesNotContain("id=\"orderBar\"", html);
        Assert.DoesNotContain("name=\"Lines[0].Quantity\"", html);
    }

    /// <summary>The ordering view shows the "Ordering for" bar, a stepper per dish, the order bar and a way out.</summary>
    [Fact]
    public async Task Menu_OrderingView_ShowsTheBarSteppersAndOrderBar()
    {
        SignedInUser customer = await SignInNewCustomerAsync();
        await AddReservationAsync(customer.Id);
        await AddDishAsync();

        string html = await customer.Client.GetStringAsync($"{MenuUrl}?order=1");

        Assert.Contains("Ordering for", html);
        Assert.Contains("id=\"orderBar\"", html);
        Assert.Contains("id=\"orderDrawer\"", html);
        Assert.Contains("name=\"Lines[0].Quantity\"", html);
        Assert.Contains("Stop ordering", html);
        Assert.Contains("Adding to", html);
    }

    /// <summary>Each dish shows how many the reservation has already ordered, and the stepper starts at zero (it adds more).</summary>
    [Fact]
    public async Task Menu_OrderingView_ShowsWhatTheReservationAlreadyOrdered()
    {
        SignedInUser customer = await SignInNewCustomerAsync();
        Reservation reservation = await AddReservationAsync(customer.Id);
        MenuItem dish = await AddDishAsync();
        await PlaceOrderOnTheOrderPageAsync(customer, reservation.Id, dish.Id, quantity: 2);

        string html = await customer.Client.GetStringAsync($"{MenuUrl}?order=1&reservationId={reservation.Id}");

        Assert.Contains("Already ordered: 2", html);
    }

    /// <summary>Orders add up: two orders of 1 and 2 give "Already ordered: 3".</summary>
    [Fact]
    public async Task Menu_OrderingView_AddsUpSeveralOrdersOfTheSameReservation()
    {
        SignedInUser customer = await SignInNewCustomerAsync();
        Reservation reservation = await AddReservationAsync(customer.Id);
        MenuItem dish = await AddDishAsync();
        await PlaceOrderOnTheOrderPageAsync(customer, reservation.Id, dish.Id, quantity: 1);
        await PlaceOrderOnTheOrderPageAsync(customer, reservation.Id, dish.Id, quantity: 2);

        string html = await customer.Client.GetStringAsync($"{MenuUrl}?order=1&reservationId={reservation.Id}");

        Assert.Contains("Already ordered: 3", html);
    }

    /// <summary>Someone else's reservation id is ignored: the page never shows another customer's orders.</summary>
    [Fact]
    public async Task Menu_OrderingView_IgnoresAnotherCustomersReservation()
    {
        SignedInUser owner = await SignInNewCustomerAsync();
        Reservation ownersReservation = await AddReservationAsync(owner.Id);
        MenuItem dish = await AddDishAsync();
        await PlaceOrderOnTheOrderPageAsync(owner, ownersReservation.Id, dish.Id, quantity: 2);
        SignedInUser stranger = await SignInNewCustomerAsync();
        await AddReservationAsync(stranger.Id);

        string html = await stranger.Client.GetStringAsync($"{MenuUrl}?order=1&reservationId={ownersReservation.Id}");

        Assert.DoesNotContain("Already ordered", html);
    }

    /// <summary>Staff and administrators do not order from the Menu page: it stays the plain menu for them.</summary>
    [Fact]
    public async Task Menu_StaffMember_SeesThePlainMenuOnly()
    {
        string email = $"staff-{Guid.NewGuid():N}@threegrills.test";
        await _factory.CreateUserAsync(email, Password, RoleNames.Staff);
        HttpClient staff = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(staff, email, Password);

        string html = await staff.GetStringAsync($"{MenuUrl}?order=1");

        Assert.DoesNotContain("Start ordering", html);
        Assert.DoesNotContain("id=\"orderBar\"", html);
        Assert.DoesNotContain("Sign in to order", html);
    }

    /// <summary>?reservationId= chooses which of the customer's own reservations the order is for.</summary>
    [Fact]
    public async Task Menu_OrderingView_UsesTheRequestedReservation()
    {
        SignedInUser customer = await SignInNewCustomerAsync();
        await AddReservationAsync(customer.Id);
        Reservation second = await AddReservationAsync(customer.Id);
        await AddDishAsync();

        string html = await customer.Client.GetStringAsync($"{MenuUrl}?order=1&reservationId={second.Id}");

        Assert.Contains($"id=\"orderReservationId\" value=\"{second.Id}\"", html);
    }

    /// <summary>Placing an order from the Menu adds another order to the same reservation and opens its details page.</summary>
    [Fact]
    public async Task PlaceOrderFromTheMenu_AddsAnOrder_AndOpensItsDetails()
    {
        SignedInUser customer = await SignInNewCustomerAsync();
        Reservation reservation = await AddReservationAsync(customer.Id);
        MenuItem dish = await AddDishAsync();
        string pageUrl = $"{MenuUrl}?order=1&reservationId={reservation.Id}";
        var fields = new Dictionary<string, string>
        {
            ["ReservationId"] = reservation.Id.ToString(),
            ["Lines[0].MenuItemId"] = dish.Id.ToString(),
            ["Lines[0].Quantity"] = "1"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(customer.Client, pageUrl, $"{MenuUrl}?handler=PlaceOrder", fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Customer/Orders/Details/", response.Headers.Location?.ToString());
    }

    /// <summary>A customer cannot place an order on someone else's reservation, even by posting its id by hand.</summary>
    [Fact]
    public async Task PlaceOrderFromTheMenu_OnAnotherCustomersReservation_IsRefused()
    {
        SignedInUser owner = await SignInNewCustomerAsync();
        Reservation ownersReservation = await AddReservationAsync(owner.Id);
        MenuItem dish = await AddDishAsync();
        SignedInUser stranger = await SignInNewCustomerAsync();
        await AddReservationAsync(stranger.Id);
        var fields = new Dictionary<string, string>
        {
            ["ReservationId"] = ownersReservation.Id.ToString(),
            ["Lines[0].MenuItemId"] = dish.Id.ToString(),
            ["Lines[0].Quantity"] = "1"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(stranger.Client, $"{MenuUrl}?order=1", $"{MenuUrl}?handler=PlaceOrder", fields);

        Assert.False(
            response.StatusCode == HttpStatusCode.Redirect
            && response.Headers.Location?.ToString().Contains("/Customer/Orders/Details/") == true);
    }

    // ============================== Helpers ==============================//

    private record SignedInUser(HttpClient Client, string Id);

    private async Task<SignedInUser> SignInNewCustomerAsync()
    {
        string email = $"customer-{Guid.NewGuid():N}@threegrills.test";
        ApplicationUser user = await _factory.CreateUserAsync(email, Password, RoleNames.Customer);
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);

        return new SignedInUser(client, user.Id);
    }

    private async Task<Reservation> AddReservationAsync(string customerId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await OrderTestData.AddReservationAsync(context, customerId, ReservationStatus.Confirmed);
    }

    private async Task<MenuItem> AddDishAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await OrderTestData.AddDishAsync(context, "Menu order dish " + Guid.NewGuid().ToString("N"), DishPrice, true);
    }

    /// <summary>Places an order the way it works today (the order page), so the Menu page has something to count.</summary>
    private static async Task PlaceOrderOnTheOrderPageAsync(SignedInUser customer, int reservationId, int dishId, int quantity)
    {
        string url = $"{NewOrderUrl}/{reservationId}";
        var fields = new Dictionary<string, string>
        {
            ["Lines[0].MenuItemId"] = dishId.ToString(),
            ["Lines[0].Quantity"] = quantity.ToString()
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(customer.Client, url, url, fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
