using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Tests.Menu;

/// <summary>
/// YJ-2 acceptance for the public customer menu page (/Menu): anyone can open it, and the search and
/// availability rules reach the screen. Owner: YJ.
/// </summary>
/// <remarks>
/// Integration tests in the same style as MenuPagesTests: the whole app starts in memory and real HTTP
/// requests are sent. All tests in this class share one database, so every dish gets a unique name.
/// </remarks>
public class MenuBrowsingPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string MenuUrl = "/Menu";

    /// <summary>Id of the seeded "Grills" category.</summary>
    private const int GrillsId = 2;

    private const decimal NormalPrice = 20.00m;

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public MenuBrowsingPagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The menu is public: a visitor who is not signed in can open it.</summary>
    [Fact]
    public async Task Visitor_CanOpenTheMenuWithoutSigningIn()
    {
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await client.GetAsync(MenuUrl);
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Our menu", html);
    }

    /// <summary>A dish shows with its name and current price.</summary>
    [Fact]
    public async Task Menu_ShowsADishWithItsPrice()
    {
        HttpClient client = _factory.CreateSecureClient();
        string name = NewDishName();
        await CreateDishAsync(name);

        string html = await client.GetStringAsync($"{MenuUrl}?q={name}");

        Assert.Contains(name, html);
        Assert.Contains("RM 20.00", html);
    }

    /// <summary>The search box shows only dishes that match.</summary>
    [Fact]
    public async Task Menu_Search_ShowsOnlyMatchingDishes()
    {
        HttpClient client = _factory.CreateSecureClient();
        string wanted = NewDishName();
        string other = NewDishName();
        await CreateDishAsync(wanted);
        await CreateDishAsync(other);

        string html = await client.GetStringAsync($"{MenuUrl}?q={wanted}");

        Assert.Contains(wanted, html);
        Assert.DoesNotContain(other, html);
    }

    /// <summary>A dish the restaurant has switched off is still listed, but marked Unavailable.</summary>
    [Fact]
    public async Task Menu_UnavailableDish_IsMarkedUnavailable()
    {
        HttpClient client = _factory.CreateSecureClient();
        string name = NewDishName();
        int id = await CreateDishAsync(name);
        await SetAvailabilityAsync(id, false);

        string html = await client.GetStringAsync($"{MenuUrl}?q={name}");

        Assert.Contains(name, html);
        Assert.Contains("Unavailable", html);
    }

    /// <summary>A name no other test uses, so tests that share the database never clash.</summary>
    private static string NewDishName()
    {
        return "Browse dish " + Guid.NewGuid().ToString("N");
    }

    /// <summary>Creates a dish straight through the service and returns its Id.</summary>
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
    private async Task SetAvailabilityAsync(int id, bool isAvailable)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IMenuService service = scope.ServiceProvider.GetRequiredService<IMenuService>();

        ServiceResult result = await service.SetAvailabilityAsync(id, isAvailable);
        Assert.True(result.Succeeded);
    }
}