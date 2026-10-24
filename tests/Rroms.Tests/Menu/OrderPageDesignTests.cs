using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;

namespace Rroms.Tests.Menu;

/// <summary>
/// ACCEPTANCE tests for the redesigned order page (order bar, summary drawer, dish details and dietary warnings).
/// Written BEFORE the new page. Owner: YJ.
/// </summary>
/// <remarks>
/// Same style as OrderPagesTests. The redesign must not change what the browser posts, so the first tests pin the
/// form field names. The dietary warning is only a guide: it must never appear without the customer's consent.
/// </remarks>
public class OrderPageDesignTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";
    private const string NewOrderUrl = "/Customer/Orders/New";
    private const decimal DishPrice = 20.00m;
    private const int AvoidedTagId = 2;

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public OrderPageDesignTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The page has the sticky order bar and the summary drawer, and the estimate starts at RM 0.00.</summary>
    [Fact]
    public async Task OrderPage_HasTheOrderBarAndDrawer_AndStartsAtZero()
    {
        SignedInUser customer = await SignInNewCustomerAsync();
        Reservation reservation = await AddReservationAsync(customer.Id);
        await AddDishAsync(NewDishName(), tagId: null);

        string html = await customer.Client.GetStringAsync($"{NewOrderUrl}/{reservation.Id}");

        Assert.Contains("id=\"orderBar\"", html);
        Assert.Contains("id=\"orderDrawer\"", html);
        Assert.Contains("id=\"orderTotal\">0.00</span>", html);
        Assert.Contains("Place order", html);
    }

    /// <summary>The redesign keeps the names of the fields the browser posts: dish id, quantity, dish note, order note.</summary>
    [Fact]
    public async Task OrderPage_KeepsTheFormFieldNames()
    {
        SignedInUser customer = await SignInNewCustomerAsync();
        Reservation reservation = await AddReservationAsync(customer.Id);
        await AddDishAsync(NewDishName(), tagId: null);

        string html = await customer.Client.GetStringAsync($"{NewOrderUrl}/{reservation.Id}");

        Assert.Contains("name=\"Lines[0].MenuItemId\"", html);
        Assert.Contains("name=\"Lines[0].Quantity\"", html);
        Assert.Contains("name=\"Lines[0].Notes\"", html);
        Assert.Contains("name=\"Notes\"", html);
    }

    /// <summary>A dish the customer cannot order is still shown, but without a quantity box.</summary>
    [Fact]
    public async Task OrderPage_UnavailableDish_IsShownWithoutAQuantityBox()
    {
        SignedInUser customer = await SignInNewCustomerAsync();
        Reservation reservation = await AddReservationAsync(customer.Id);
        string name = NewDishName();
        MenuItem dish = await AddDishAsync(name, tagId: null, isAvailable: false);

        string html = await customer.Client.GetStringAsync($"{NewOrderUrl}/{reservation.Id}");

        Assert.Contains(name, html);
        Assert.DoesNotContain($"id=\"quantity-{dish.Id}\"", html);
        Assert.Contains("Unavailable", html);
    }

    /// <summary>A customer who agreed to share dietary preferences sees a banner and a warning on a matching dish.</summary>
    [Fact]
    public async Task OrderPage_CustomerAvoidingATag_SeesTheBannerAndTheWarning()
    {
        SignedInUser customer = await SignInNewCustomerAsync(avoidTagId: AvoidedTagId, consent: true);
        Reservation reservation = await AddReservationAsync(customer.Id);
        string tagName = await TagNameAsync(AvoidedTagId);
        await AddDishAsync(NewDishName(), AvoidedTagId);

        string html = await customer.Client.GetStringAsync($"{NewOrderUrl}/{reservation.Id}");

        Assert.Contains("Your profile avoids", html);
        Assert.Contains($"Contains {tagName} (on your avoid list)", html);
    }

    /// <summary>Tags stored without consent must never be used, so such a customer sees no warning.</summary>
    [Fact]
    public async Task OrderPage_AvoidedTagWithoutConsent_ShowsNoWarning()
    {
        SignedInUser customer = await SignInNewCustomerAsync(avoidTagId: AvoidedTagId, consent: false);
        Reservation reservation = await AddReservationAsync(customer.Id);
        await AddDishAsync(NewDishName(), AvoidedTagId);

        string html = await customer.Client.GetStringAsync($"{NewOrderUrl}/{reservation.Id}");

        Assert.DoesNotContain("Your profile avoids", html);
        Assert.DoesNotContain("on your avoid list", html);
    }

    /// <summary>A customer who avoids nothing sees no banner, even when dishes carry dietary tags.</summary>
    [Fact]
    public async Task OrderPage_CustomerAvoidingNothing_ShowsNoWarning()
    {
        SignedInUser customer = await SignInNewCustomerAsync();
        Reservation reservation = await AddReservationAsync(customer.Id);
        await AddDishAsync(NewDishName(), AvoidedTagId);

        string html = await customer.Client.GetStringAsync($"{NewOrderUrl}/{reservation.Id}");

        Assert.DoesNotContain("Your profile avoids", html);
        Assert.DoesNotContain("on your avoid list", html);
    }

    /// <summary>The warning is only a guide: the customer can still order the dish.</summary>
    [Fact]
    public async Task PlaceOrder_DishWithAnAvoidedTag_StillWorks()
    {
        SignedInUser customer = await SignInNewCustomerAsync(avoidTagId: AvoidedTagId, consent: true);
        Reservation reservation = await AddReservationAsync(customer.Id);
        MenuItem dish = await AddDishAsync(NewDishName(), AvoidedTagId);
        string url = $"{NewOrderUrl}/{reservation.Id}";
        var fields = new Dictionary<string, string>
        {
            ["Lines[0].MenuItemId"] = dish.Id.ToString(),
            ["Lines[0].Quantity"] = "1"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(customer.Client, url, url, fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Customer/Orders/Details/", response.Headers.Location?.ToString());
    }

    // ============================== Helpers ==============================//

    private record SignedInUser(HttpClient Client, string Id);

    /// <summary>Creates a customer, optionally with one avoided tag (and the consent date), and signs in.</summary>
    private async Task<SignedInUser> SignInNewCustomerAsync(int? avoidTagId = null, bool consent = false)
    {
        string email = $"customer-{Guid.NewGuid():N}@threegrills.test";
        ApplicationUser user = await _factory.CreateUserAsync(email, Password, RoleNames.Customer);

        if (avoidTagId != null)
        {
            using IServiceScope scope = _factory.Services.CreateScope();
            ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            ApplicationUser stored = await context.Users
                .Include(u => u.AvoidedDietaryTags)
                .SingleAsync(u => u.Id == user.Id);
            stored.AvoidedDietaryTags.Add(await context.DietaryTags.SingleAsync(t => t.Id == avoidTagId));
            stored.DietaryConsentAtUtc = consent ? DateTime.UtcNow : null;
            await context.SaveChangesAsync();
        }

        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);

        return new SignedInUser(client, user.Id);
    }

    private static string NewDishName()
    {
        return "Design dish " + Guid.NewGuid().ToString("N");
    }

    private async Task<string> TagNameAsync(int tagId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.DietaryTags.Where(t => t.Id == tagId).Select(t => t.Name).SingleAsync();
    }

    private async Task<Reservation> AddReservationAsync(string customerId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await OrderTestData.AddReservationAsync(context, customerId, ReservationStatus.Confirmed);
    }

    /// <summary>Adds a dish, optionally carrying one dietary tag.</summary>
    private async Task<MenuItem> AddDishAsync(string name, int? tagId, bool isAvailable = true)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        MenuItem dish = await OrderTestData.AddDishAsync(context, name, DishPrice, isAvailable);

        if (tagId != null)
        {
            MenuItem stored = await context.MenuItems
                .Include(item => item.DietaryTags)
                .SingleAsync(item => item.Id == dish.Id);
            stored.DietaryTags.Add(await context.DietaryTags.SingleAsync(t => t.Id == tagId));
            await context.SaveChangesAsync();
        }

        return dish;
    }
}
