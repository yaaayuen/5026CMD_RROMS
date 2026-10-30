using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Services;

using Xunit;

namespace Rroms.Tests.Menu;

/// <summary>
/// ACCEPTANCE tests for the admin Discounts pages and for charging the discounted price (YJ),
/// written BEFORE the pages they test. Owner: YJ.
/// </summary>
/// <remarks>All tests share one database, so every name is unique.</remarks>
public class DiscountPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";
    private const string ListUrl = "/Admin/Discounts";
    private const string EditUrl = "/Admin/Discounts/Edit";

    private readonly TestWebApplicationFactory _factory;

    public DiscountPagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ============================== Access control ==============================//

    [Theory]
    [InlineData(ListUrl)]
    [InlineData(EditUrl)]
    public async Task Anonymous_IsSentToLogin(string url)
    {
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData(RoleNames.Customer, ListUrl)]
    [InlineData(RoleNames.Staff, ListUrl)]
    [InlineData(RoleNames.Customer, EditUrl)]
    public async Task NonAdministrators_AreDenied(string role, string url)
    {
        HttpClient client = await SignInAsync(role);

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData(ListUrl)]
    [InlineData(EditUrl)]
    public async Task Administrator_CanOpenEveryDiscountPage(string url)
    {
        HttpClient client = await SignInAsync(RoleNames.Administrator);

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminDashboard_LinksToDiscounts()
    {
        HttpClient client = await SignInAsync(RoleNames.Administrator);

        // The dashboard groups Promotions under "Offers and news".
        string html = await client.GetStringAsync("/Admin/OffersNews");

        Assert.Contains("href=\"/Admin/Discounts\"", html);
    }

    // ============================== Creating ==============================//

    [Fact]
    public async Task Create_ValidForm_SavesAndShowsInTheList()
    {
        HttpClient client = await SignInAsync(RoleNames.Administrator);
        string name = "Lunch " + Guid.NewGuid().ToString("N");
        var fields = new Dictionary<string, string>
        {
            ["Input.Name"] = name,
            ["Input.Percent"] = "15",
            ["Input.Scope"] = "WholeMenu",
            ["Input.SelectedDays"] = "2",
            ["Input.FromTime"] = "11:00",
            ["Input.ToTime"] = "15:00",
            ["Input.IsActive"] = "true"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(client, EditUrl, EditUrl, fields);
        string list = await client.GetStringAsync(ListUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(name, list);
        MenuDiscount saved = await FindDiscountAsync(name);
        Assert.Equal(15m, saved.Percent);
        Assert.Equal(DiscountDays.Bit(DayOfWeek.Monday), saved.DaysOfWeek);
        Assert.Equal(11 * 60, saved.FromMinute);
        Assert.Equal(15 * 60, saved.ToMinute);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("91")]
    [InlineData("101")]
    public async Task Create_PercentOutOfRange_IsRefusedAndNothingIsSaved(string percent)
    {
        HttpClient client = await SignInAsync(RoleNames.Administrator);
        string name = "Bad percent " + Guid.NewGuid().ToString("N");
        var fields = new Dictionary<string, string>
        {
            ["Input.Name"] = name,
            ["Input.Percent"] = percent,
            ["Input.Scope"] = "WholeMenu",
            ["Input.IsActive"] = "true"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(client, EditUrl, EditUrl, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await DiscountExistsAsync(name));
    }

    [Fact]
    public async Task Create_CategoryScope_SavesTheChosenCategory()
    {
        HttpClient client = await SignInAsync(RoleNames.Administrator);
        string name = "Category promo " + Guid.NewGuid().ToString("N");
        var fields = new Dictionary<string, string>
        {
            ["Input.Name"] = name,
            ["Input.Percent"] = "20",
            ["Input.Scope"] = "Category",
            ["Input.CategoryIds"] = "3",
            ["Input.IsActive"] = "true"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(client, EditUrl, EditUrl, fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        MenuDiscount saved = await FindDiscountAsync(name);
        Assert.Equal(DiscountScope.Category, saved.Scope);
        Assert.Equal(3, saved.Categories.Single().Id);
    }

    [Fact]
    public async Task Create_CategoryScopeWithoutACategory_IsRefused()
    {
        HttpClient client = await SignInAsync(RoleNames.Administrator);
        string name = "No category " + Guid.NewGuid().ToString("N");
        var fields = new Dictionary<string, string>
        {
            ["Input.Name"] = name,
            ["Input.Percent"] = "10",
            ["Input.Scope"] = "Category",
            ["Input.IsActive"] = "true"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(client, EditUrl, EditUrl, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await DiscountExistsAsync(name));
    }

    [Fact]
    public async Task Create_HoursEndingBeforeTheyStart_AreRefused()
    {
        HttpClient client = await SignInAsync(RoleNames.Administrator);
        string name = "Bad hours " + Guid.NewGuid().ToString("N");
        var fields = new Dictionary<string, string>
        {
            ["Input.Name"] = name,
            ["Input.Percent"] = "10",
            ["Input.Scope"] = "WholeMenu",
            ["Input.FromTime"] = "15:00",
            ["Input.ToTime"] = "11:00",
            ["Input.IsActive"] = "true"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(client, EditUrl, EditUrl, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await DiscountExistsAsync(name));
    }

    // ============================== What customers see ==============================//

    [Fact]
    public async Task Menu_ShowsTheBannerOfARunningPromotion_ToAnyone()
    {
        string name = "Banner promo " + Guid.NewGuid().ToString("N");
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.MenuDiscounts.Add(new MenuDiscount { Name = name, Percent = 15m, Scope = DiscountScope.WholeMenu, IsActive = true });
            await context.SaveChangesAsync();
        }
        HttpClient visitor = _factory.CreateSecureClient();

        string html = await visitor.GetStringAsync("/Menu");

        Assert.Contains(name + ": 15% off the whole menu", html);
    }

    [Fact]
    public async Task Menu_DoesNotShowASwitchedOffPromotion()
    {
        string name = "Off promo " + Guid.NewGuid().ToString("N");
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.MenuDiscounts.Add(new MenuDiscount { Name = name, Percent = 15m, Scope = DiscountScope.WholeMenu, IsActive = false });
            await context.SaveChangesAsync();
        }
        HttpClient visitor = _factory.CreateSecureClient();

        string html = await visitor.GetStringAsync("/Menu");

        Assert.DoesNotContain(name, html);
    }

    // ============================== Charging the discounted price ==============================//

    [Fact]
    public async Task PlaceOrder_ChargesTheDiscountedPrice()
    {
        string email = $"customer-{Guid.NewGuid():N}@threegrills.test";
        ApplicationUser user = await _factory.CreateUserAsync(email, Password, RoleNames.Customer);
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);
        int reservationId;
        int dishId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Reservation reservation = await OrderTestData.AddReservationAsync(context, user.Id, ReservationStatus.Confirmed);
            MenuItem dish = await OrderTestData.AddDishAsync(context, "Discount dish " + Guid.NewGuid().ToString("N"), 20.00m, true);
            context.MenuDiscounts.Add(new MenuDiscount
            {
                Name = "Everything " + Guid.NewGuid().ToString("N"),
                Percent = 15m,
                Scope = DiscountScope.Dishes,
                Dishes = new List<MenuItem> { context.MenuItems.Single(item => item.Id == dish.Id) },
                IsActive = true
            });
            await context.SaveChangesAsync();
            reservationId = reservation.Id;
            dishId = dish.Id;
        }
        string url = $"/Customer/Orders/New/{reservationId}";
        var fields = new Dictionary<string, string>
        {
            ["Lines[0].MenuItemId"] = dishId.ToString(),
            ["Lines[0].Quantity"] = "1",
            ["Lines[0].UnitPrice"] = "0.01"
        };

        await FormHelper.PostFormAsync(client, url, url, fields);

        using IServiceScope check = _factory.Services.CreateScope();
        ApplicationDbContext db = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Order order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.ReservationId == reservationId);
        Assert.Equal(17.00m, order.Items.Single().UnitPrice);
    }

    // ============================== Helpers ==============================//

    private async Task<HttpClient> SignInAsync(string role)
    {
        string email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@threegrills.test";
        await _factory.CreateUserAsync(email, Password, role);
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, Password);

        return client;
    }

    private async Task<MenuDiscount> FindDiscountAsync(string name)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.MenuDiscounts.Include(d => d.Categories).SingleAsync(d => d.Name == name);
    }

    private async Task<bool> DiscountExistsAsync(string name)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.MenuDiscounts.AnyAsync(d => d.Name == name);
    }
}
