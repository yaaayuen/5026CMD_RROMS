using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.Menu;

/// <summary>
/// Clicking a dish card on the plain menu (Part 19, Alex): a customer with a booking goes straight into the ordering view
/// with that dish already picked; a customer without one is asked to book a table; a visitor is asked to sign in.
/// </summary>
public class MenuClickToOrderTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";
    private const string MenuUrl = "/Menu";

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public MenuClickToOrderTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>A customer with a booking: each card links to the ordering view for that booking and that dish.</summary>
    [Fact]
    public async Task PlainMenu_CustomerWithABooking_CardsLinkToOrderingThatDish()
    {
        (HttpClient client, string customerId) = await SignInNewCustomerAsync();
        Reservation reservation = await AddReservationAsync(customerId);
        MenuItem dish = await AddDishAsync();

        string html = await client.GetStringAsync(MenuUrl);

        Assert.Contains($"href=\"/Menu?order=1&amp;reservationId={reservation.Id}&amp;dish={dish.Id}\"", html);
        Assert.Contains("Tap to order", html);
        Assert.DoesNotContain("id=\"orderPrompt\"", html);
    }

    /// <summary>Opening the ordering view from a card starts with one of that dish and says which table it is for.</summary>
    [Fact]
    public async Task OrderingView_FromACard_StartsWithOneOfThatDish()
    {
        (HttpClient client, string customerId) = await SignInNewCustomerAsync();
        Reservation reservation = await AddReservationAsync(customerId);
        MenuItem dish = await AddDishAsync();

        string html = await client.GetStringAsync($"{MenuUrl}?order=1&reservationId={reservation.Id}&dish={dish.Id}");

        Assert.Matches(new Regex($"id=\"quantity-{dish.Id}\"[^>]*value=\"1\""), html);
        Assert.Contains("added to your order for the table above", html);
        Assert.Contains("mn-ordering-just-started", html);
    }

    /// <summary>A dish id that is not on the menu changes nothing: no note, nothing picked.</summary>
    [Fact]
    public async Task OrderingView_WithAnUnknownDish_PicksNothing()
    {
        (HttpClient client, string customerId) = await SignInNewCustomerAsync();
        Reservation reservation = await AddReservationAsync(customerId);
        MenuItem dish = await AddDishAsync();

        string html = await client.GetStringAsync($"{MenuUrl}?order=1&reservationId={reservation.Id}&dish=999999");

        Assert.Matches(new Regex($"id=\"quantity-{dish.Id}\"[^>]*value=\"0\""), html);
        Assert.DoesNotContain("added to your order for the table above", html);
    }

    /// <summary>A customer without a booking that can order is asked to book a table first.</summary>
    [Fact]
    public async Task PlainMenu_CustomerWithoutABooking_IsAskedToBookFirst()
    {
        (HttpClient client, _) = await SignInNewCustomerAsync();
        await AddDishAsync();

        string html = await client.GetStringAsync(MenuUrl);

        Assert.Contains("id=\"orderPrompt\"", html);
        Assert.Contains("Book a table first", html);
        Assert.Contains("js-order-prompt\" href=\"/Customer/Reservations/Book\"", html);
    }

    /// <summary>A visitor is asked to sign in when clicking a dish.</summary>
    [Fact]
    public async Task PlainMenu_Visitor_IsAskedToSignIn()
    {
        await AddDishAsync();
        HttpClient visitor = _factory.CreateSecureClient();

        string html = await visitor.GetStringAsync(MenuUrl);

        Assert.Contains("id=\"orderPrompt\"", html);
        Assert.Contains("js-order-prompt\" href=\"/Account/Login?returnUrl=%2FMenu\"", html);
    }

    /// <summary>Staff and administrators keep a display-only menu.</summary>
    [Fact]
    public async Task PlainMenu_Staff_CardsAreNotClickable()
    {
        await AddDishAsync();
        string email = $"staff-{Guid.NewGuid():N}@threegrills.test";
        await _factory.CreateUserAsync(email, Password, RoleNames.Staff);
        HttpClient staff = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(staff, email, Password);

        string html = await staff.GetStringAsync(MenuUrl);

        Assert.DoesNotContain("Tap to order", html);
        Assert.DoesNotContain("id=\"orderPrompt\"", html);
    }

    private async Task<(HttpClient Client, string Id)> SignInNewCustomerAsync()
    {
        string email = $"customer-{Guid.NewGuid():N}@threegrills.test";
        ApplicationUser user = await _factory.CreateUserAsync(email, Password, RoleNames.Customer);
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);

        return (client, user.Id);
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

        return await OrderTestData.AddDishAsync(context, "Click dish " + Guid.NewGuid().ToString("N"), 20.00m, true);
    }
}
