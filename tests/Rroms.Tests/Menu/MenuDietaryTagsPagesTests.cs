using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Tests.Menu;

/// <summary>
/// YJ-4 acceptance for the dietary tags on the pages: the admin form saves them, and the public menu shows
/// badges and can filter to vegetarian dishes. Owner: YJ.
/// </summary>
/// <remarks>
/// Integration tests in the same style as MenuPagesTests. All tests in this class share one database,
/// so every dish gets a unique name.
/// </remarks>
public class MenuDietaryTagsPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string EditUrl = "/Admin/Menu/Edit";
    private const string MenuUrl = "/Menu";

    /// <summary>Id of the seeded "Grills" category.</summary>
    private const int GrillsId = 2;

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public MenuDietaryTagsPagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The admin form saves the two dietary tags.</summary>
    [Fact]
    public async Task AdminForm_SavesTheDietaryTags()
    {
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        string name = NewDishName();
        var fields = new Dictionary<string, string>
        {
            ["Input.Name"] = name,
            ["Input.Description"] = "Made for a test",
            ["Input.MenuCategoryId"] = GrillsId.ToString(),
            ["Input.Price"] = "20.00",
            ["Input.IsAvailable"] = "true",
            ["Input.IsVegetarian"] = "true",
            ["Input.IsSpicy"] = "true"
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(client, EditUrl, EditUrl, fields);
        MenuItem? saved = await FindDishAsync(name);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(saved);
        Assert.True(saved.IsVegetarian);
        Assert.True(saved.IsSpicy);
    }

    /// <summary>The public menu shows a "Vegetarian" badge and a "Spicy" badge on tagged dishes.</summary>
    [Fact]
    public async Task Menu_ShowsBadgesForTaggedDishes()
    {
        HttpClient client = _factory.CreateSecureClient();
        string name = NewDishName();
        await CreateDishAsync(name, isVegetarian: true, isSpicy: true);

        string html = await client.GetStringAsync($"{MenuUrl}?q={name}");

        // The badge text is followed directly by the closing tag, unlike the filter label "Vegetarian only".
        Assert.Contains(">Vegetarian</span>", html);
        Assert.Contains(">Spicy</span>", html);
    }

    /// <summary>"Vegetarian only" hides dishes that are not vegetarian.</summary>
    [Fact]
    public async Task Menu_VegetarianOnly_HidesOtherDishes()
    {
        HttpClient client = _factory.CreateSecureClient();
        string token = Guid.NewGuid().ToString("N");
        string vegetarianName = "Veg " + token;
        string otherName = "Meat " + token;
        await CreateDishAsync(vegetarianName, isVegetarian: true, isSpicy: false);
        await CreateDishAsync(otherName, isVegetarian: false, isSpicy: false);

        string html = await client.GetStringAsync($"{MenuUrl}?q={token}&vegetarian=true");

        Assert.Contains(vegetarianName, html);
        Assert.DoesNotContain(otherName, html);
    }

    private static string NewDishName()
    {
        return "Tag dish " + Guid.NewGuid().ToString("N");
    }

    /// <summary>Creates a dish straight through the service.</summary>
    private async Task CreateDishAsync(string name, bool isVegetarian, bool isSpicy)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IMenuService service = scope.ServiceProvider.GetRequiredService<IMenuService>();

        var input = new MenuItemInput
        {
            Name = name,
            Description = "Made for a test",
            MenuCategoryId = GrillsId,
            Price = 20.00m,
            IsAvailable = true,
            IsVegetarian = isVegetarian,
            IsSpicy = isSpicy
        };

        ServiceResult<int> result = await service.CreateMenuItemAsync(input);
        Assert.True(result.Succeeded);
    }

    /// <summary>Reads a dish back by its unique name, or null when it does not exist.</summary>
    private async Task<MenuItem?> FindDishAsync(string name)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IMenuService service = scope.ServiceProvider.GetRequiredService<IMenuService>();

        IReadOnlyList<MenuItem> items = await service.GetMenuItemsAsync(null);

        return items.FirstOrDefault(item => item.Name == name);
    }
}
