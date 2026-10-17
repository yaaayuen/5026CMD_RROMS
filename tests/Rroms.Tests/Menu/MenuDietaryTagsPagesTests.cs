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
/// them as badges and can hide dishes that contain a tag. Owner: YJ.
/// </summary>
/// <remarks>
/// Integration tests in the same style as MenuPagesTests. All tests in this class share one database,
/// so every dish gets a unique name. The three tags are YY's seeded rows: 1 animal products, 2 dairy, 3 spicy.
/// </remarks>
public class MenuDietaryTagsPagesTests : IClassFixture<TestWebApplicationFactory>
{
    private const string EditUrl = "/Admin/Menu/Edit";
    private const string MenuUrl = "/Menu";

    /// <summary>Id of the seeded "Grills" category.</summary>
    private const int GrillsId = 2;

    private const int AnimalProductsTagId = 1;
    private const int DairyTagId = 2;
    private const int SpicyTagId = 3;

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public MenuDietaryTagsPagesTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The admin form saves the ticked dietary tags.</summary>
    [Fact]
    public async Task AdminForm_SavesTheTickedDietaryTags()
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
            // A list is sent as numbered fields, because a dictionary cannot repeat a name.
            ["Input.DietaryTagIds[0]"] = DairyTagId.ToString(),
            ["Input.DietaryTagIds[1]"] = SpicyTagId.ToString()
        };

        HttpResponseMessage response = await FormHelper.PostFormAsync(client, EditUrl, EditUrl, fields);
        MenuItem? saved = await FindDishAsync(name);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(saved);
        Assert.Equal(new List<int> { DairyTagId, SpicyTagId }, saved.DietaryTags.Select(tag => tag.Id).OrderBy(id => id).ToList());
    }

    /// <summary>The admin form lists YY's three tags as tick boxes.</summary>
    [Fact]
    public async Task AdminForm_ListsTheThreeTags()
    {
        HttpClient client = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(client, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);

        string html = await client.GetStringAsync(EditUrl);

        Assert.Contains(">Animal products</label>", html);
        Assert.Contains(">Dairy</label>", html);
        Assert.Contains(">Spicy</label>", html);
    }

    /// <summary>The public menu shows a badge for every tag a dish contains.</summary>
    [Fact]
    public async Task Menu_ShowsBadgesForTaggedDishes()
    {
        HttpClient client = _factory.CreateSecureClient();
        string name = NewDishName();
        await CreateDishAsync(name, DairyTagId, SpicyTagId);

        string html = await client.GetStringAsync($"{MenuUrl}?q={name}");

        // A badge text is followed directly by the closing span tag, unlike the filter labels (closing label tag).
        Assert.Contains(">Dairy</span>", html);
        Assert.Contains(">Spicy</span>", html);
        Assert.DoesNotContain(">Animal products</span>", html);
    }

    /// <summary>"Hide dishes that contain" hides a dish with that tag and keeps the others.</summary>
    [Fact]
    public async Task Menu_HidingATag_HidesDishesThatContainIt()
    {
        HttpClient client = _factory.CreateSecureClient();
        string token = Guid.NewGuid().ToString("N");
        string dairyName = "Dairy " + token;
        string plainName = "Plain " + token;
        await CreateDishAsync(dairyName, DairyTagId);
        await CreateDishAsync(plainName);

        string html = await client.GetStringAsync($"{MenuUrl}?q={token}&hide={DairyTagId}");

        Assert.Contains(plainName, html);
        Assert.DoesNotContain(dairyName, html);
    }

    /// <summary>Hiding two tags hides a dish that has either of them.</summary>
    [Fact]
    public async Task Menu_HidingTwoTags_HidesDishesWithEither()
    {
        HttpClient client = _factory.CreateSecureClient();
        string token = Guid.NewGuid().ToString("N");
        string spicyName = "Spicy " + token;
        string meatName = "Meat " + token;
        string plainName = "Plain " + token;
        await CreateDishAsync(spicyName, SpicyTagId);
        await CreateDishAsync(meatName, AnimalProductsTagId);
        await CreateDishAsync(plainName);

        string html = await client.GetStringAsync($"{MenuUrl}?q={token}&hide={SpicyTagId}&hide={AnimalProductsTagId}");

        Assert.Contains(plainName, html);
        Assert.DoesNotContain(spicyName, html);
        Assert.DoesNotContain(meatName, html);
    }

    /// <summary>The filter form offers one tick box for each tag.</summary>
    [Fact]
    public async Task Menu_FilterForm_OffersTheThreeTags()
    {
        HttpClient client = _factory.CreateSecureClient();

        string html = await client.GetStringAsync(MenuUrl);

        Assert.Contains("Hide dishes that contain", html);
        Assert.Contains(">Animal products</label>", html);
        Assert.Contains(">Dairy</label>", html);
        Assert.Contains(">Spicy</label>", html);
    }

    private static string NewDishName()
    {
        return "Tag dish " + Guid.NewGuid().ToString("N");
    }

    /// <summary>Creates a dish straight through the service.</summary>
    private async Task CreateDishAsync(string name, params int[] tagIds)
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
            DietaryTagIds = tagIds.ToList()
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
