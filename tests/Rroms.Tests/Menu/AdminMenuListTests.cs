using System.Net;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Tests.Menu;

/// <summary>
/// YJ-1 acceptance for the improved admin menu list (/Admin/Menu): dish count, search, status filter,
/// the availability switch, the delete confirmation and the "price now" column. Owner: YJ.
/// </summary>
/// <remarks>
/// Same style as <see cref="MenuPagesTests"/>: the whole app runs in memory and tests send real HTTP requests.
/// All tests share one database, so every dish gets a unique name and searches use that name.
/// </remarks>
public class AdminMenuListTests : IClassFixture<TestWebApplicationFactory>
{
    private const string ListUrl = "/Admin/Menu";
    private const int GrillsId = 2;
    private const decimal NormalPrice = 20.00m;

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public AdminMenuListTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The heading shows how many dishes are listed.</summary>
    [Fact]
    public async Task List_HeadingShowsTheDishCount()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        await CreateDishAsync(NewName());

        string html = await client.GetStringAsync(ListUrl);

        Assert.Matches(new Regex(@"All dishes \(\d+\)"), html);
    }

    /// <summary>Searching by name shows only the dishes whose name contains the text.</summary>
    [Fact]
    public async Task Search_ByName_ShowsOnlyMatchingDishes()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string wanted = NewName();
        string other = NewName();
        await CreateDishAsync(wanted);
        await CreateDishAsync(other);

        string html = await client.GetStringAsync($"{ListUrl}?q={wanted}");

        Assert.Contains(wanted, html);
        Assert.DoesNotContain(other, html);
    }

    /// <summary>The search ignores upper and lower case.</summary>
    [Fact]
    public async Task Search_IgnoresCase()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewName();
        await CreateDishAsync(name);

        string html = await client.GetStringAsync($"{ListUrl}?q={name.ToUpperInvariant()}");

        Assert.Contains(name, html);
    }

    /// <summary>A search that matches nothing says so and offers to clear the filters.</summary>
    [Fact]
    public async Task Search_WithNoMatch_ShowsAMessage()
    {
        HttpClient client = await SignInAsAdministratorAsync();

        string html = await client.GetStringAsync($"{ListUrl}?q=zzz-{Guid.NewGuid():N}");

        Assert.Contains("No dishes match your search", html);
    }

    /// <summary>The status filter "unavailable" hides dishes customers can order, and "available" hides the others.</summary>
    [Fact]
    public async Task StatusFilter_SplitsAvailableAndUnavailable()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string onName = NewName();
        string offName = NewName();
        await CreateDishAsync(onName);
        int offId = await CreateDishAsync(offName);
        await SetAvailabilityAsync(offId, false);

        string unavailable = await client.GetStringAsync($"{ListUrl}?status=unavailable&q={offName}");
        string unavailableOnly = await client.GetStringAsync($"{ListUrl}?status=unavailable&q={onName}");
        string available = await client.GetStringAsync($"{ListUrl}?status=available&q={onName}");
        string availableOnly = await client.GetStringAsync($"{ListUrl}?status=available&q={offName}");

        Assert.Contains($"data-dish=\"{offName}\"", unavailable);
        Assert.DoesNotContain($"data-dish=\"{onName}\"", unavailableOnly);
        Assert.Contains($"data-dish=\"{onName}\"", available);
        Assert.DoesNotContain($"data-dish=\"{offName}\"", availableOnly);
    }

    /// <summary>An unknown status value is ignored instead of hiding everything.</summary>
    [Fact]
    public async Task StatusFilter_UnknownValue_ShowsEverything()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewName();
        await CreateDishAsync(name);

        string html = await client.GetStringAsync($"{ListUrl}?status=banana&q={name}");

        Assert.Contains(name, html);
    }

    /// <summary>When a filter is on, the heading also shows how many dishes there are in total.</summary>
    [Fact]
    public async Task Filtered_HeadingShowsTheTotal()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewName();
        await CreateDishAsync(name);

        string html = await client.GetStringAsync($"{ListUrl}?q={name}");

        Assert.Contains("All dishes (1)", html);
        Assert.Matches(new Regex(@"of \d+"), html);
    }

    /// <summary>Each dish has an availability switch that shows its state.</summary>
    [Fact]
    public async Task EachDish_HasAnAvailabilitySwitch()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewName();
        await CreateDishAsync(name);

        string html = await client.GetStringAsync($"{ListUrl}?q={name}");

        Assert.Contains("role=\"switch\"", html);
        Assert.Contains("aria-checked=\"true\"", html);
    }

    /// <summary>After switching a dish, the administrator comes back to the same search and filters.</summary>
    [Fact]
    public async Task Toggle_KeepsTheSearchAndFilters()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewName();
        int id = await CreateDishAsync(name);
        var fields = new Dictionary<string, string> { ["makeAvailable"] = "false" };

        HttpResponseMessage response = await FormHelper.PostFormAsync(
            client, ListUrl, $"{ListUrl}?handler=Toggle&id={id}&q={name}&status=available", fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        string location = response.Headers.Location?.ToString() ?? string.Empty;
        Assert.Contains($"q={name}", location);
        Assert.Contains("status=available", location);
    }

    /// <summary>The delete button opens a confirmation first: the page carries the dialog and names the dish on the button.</summary>
    [Fact]
    public async Task DeleteButton_HasAConfirmationDialog()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewName();
        await CreateDishAsync(name);

        string html = await client.GetStringAsync($"{ListUrl}?q={name}");

        Assert.Contains("id=\"deleteDialog\"", html);
        Assert.Contains($"data-dish=\"{name}\"", html);
        Assert.Contains($"aria-label=\"Delete {name}\"", html);
        Assert.Contains("Yes, delete", html);
    }

    /// <summary>A dish without a photo shows a coloured tile with its first letter.</summary>
    [Fact]
    public async Task DishWithoutPhoto_ShowsALetterTile()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewName();
        await CreateDishAsync(name);

        string html = await client.GetStringAsync($"{ListUrl}?q={name}");

        Assert.Contains("class=\"am-tile\"", html);
    }

    /// <summary>A dish with no promotion shows its normal price and no tag; the column is called "Price now".</summary>
    [Fact]
    public async Task PriceColumn_ShowsTheNormalPriceWhenThereIsNoPromotion()
    {
        HttpClient client = await SignInAsAdministratorAsync();
        string name = NewName();
        await CreateDishAsync(name);

        string html = await client.GetStringAsync($"{ListUrl}?q={name}");

        Assert.Contains("Price now (RM)", html);
        Assert.Contains(NormalPrice.ToString("N2"), html);
        Assert.DoesNotContain("am-was", html.Substring(html.IndexOf("<tbody", StringComparison.Ordinal)));
    }

    /// <summary>The service prices a dish like the customer menu: with no promotion it is the normal price.</summary>
    [Fact]
    public async Task GetCurrentPrices_WithoutPromotion_IsTheNormalPrice()
    {
        string name = NewName();
        int id = await CreateDishAsync(name);

        using IServiceScope scope = _factory.Services.CreateScope();
        IMenuService service = scope.ServiceProvider.GetRequiredService<IMenuService>();
        MenuItem? item = await service.GetMenuItemAsync(id);
        Assert.NotNull(item);

        IReadOnlyDictionary<int, PriceQuote> quotes = await service.GetCurrentPricesAsync(new[] { item });

        Assert.Equal(NormalPrice, quotes[id].Price);
        Assert.False(quotes[id].IsOffer);
    }

    /// <summary>Customers and staff still cannot open the list.</summary>
    [Theory]
    [InlineData(RoleNames.Customer)]
    [InlineData(RoleNames.Staff)]
    public async Task NonAdministrator_IsStillDenied(string roleName)
    {
        string email = $"{roleName.ToLowerInvariant()}-{Guid.NewGuid():N}@threegrills.test";
        await _factory.CreateUserAsync(email, "Grill!Test2026", roleName);
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, email, "Grill!Test2026");

        HttpResponseMessage response = await client.GetAsync(ListUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    // ============================== Helpers ==============================//

    /// <summary>A name no other test uses. Letters and digits only, so it is safe in an address.</summary>
    private static string NewName()
    {
        return "amtest" + Guid.NewGuid().ToString("N");
    }

    /// <summary>Returns a client signed in as the seeded administrator.</summary>
    private async Task<HttpClient> SignInAsAdministratorAsync()
    {
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        return client;
    }

    /// <summary>Creates a dish through the service and returns its Id.</summary>
    /// <param name="name">The unique dish name.</param>
    private async Task<int> CreateDishAsync(string name)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IMenuService service = scope.ServiceProvider.GetRequiredService<IMenuService>();

        var input = new MenuItemInput
        {
            Name = name,
            Description = "Made for a test",
            MenuCategoryId = GrillsId,
            Price = NormalPrice,
            IsAvailable = true
        };

        ServiceResult<int> result = await service.CreateMenuItemAsync(input);
        Assert.True(result.Succeeded);

        return result.Value;
    }

    /// <summary>Turns ordering on or off for a dish through the service.</summary>
    /// <param name="id">The dish.</param>
    /// <param name="isAvailable">True to allow ordering.</param>
    private async Task SetAvailabilityAsync(int id, bool isAvailable)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IMenuService service = scope.ServiceProvider.GetRequiredService<IMenuService>();

        ServiceResult result = await service.SetAvailabilityAsync(id, isAvailable);
        Assert.True(result.Succeeded);
    }
}
