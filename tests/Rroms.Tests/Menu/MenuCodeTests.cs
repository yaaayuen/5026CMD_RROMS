using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Rroms.Web.Common;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Tests.Menu;

/// <summary>
/// Acceptance tests for the dish codes (S01, G02, P01 ...) made by <see cref="MenuService"/>.
/// </summary>
/// <remarks>
/// Seed data in every test: Starters (Id 1), Grills (Id 2), Pasta (Id 3), Desserts (Id 4) and Beverages (Id 5),
/// so their codes start with S, G, P, D and B. No menu items are seeded.
/// </remarks>
public class MenuCodeTests : IDisposable
{
    private const int StartersId = 1;
    private const int GrillsId = 2;
    private const int PastaId = 3;
    private const int BeveragesId = 5;
    private const decimal NormalPrice = 20.00m;

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly RestaurantService _restaurantService;
    private readonly MenuService _service;

    /// <summary>Runs before EVERY test: a brand-new in-memory database.</summary>
    public MenuCodeTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _restaurantService = new RestaurantService(_context);
        _service = new MenuService(_context, _restaurantService, new StubImageStorage());
    }

    /// <summary>Runs after EVERY test: closing the connection deletes the database.</summary>
    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>Creates a dish through the service and returns its Id.</summary>
    private async Task<int> AddDishAsync(string name, int categoryId)
    {
        MenuItemInput input = new MenuItemInput
        {
            Name = name,
            Description = "Plain description",
            MenuCategoryId = categoryId,
            Price = NormalPrice,
            IsAvailable = true
        };

        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        Assert.True(result.Succeeded);

        return result.Value;
    }

    /// <summary>Reads a dish's code back from the database.</summary>
    private async Task<string?> CodeOfAsync(int id)
    {
        MenuItem? item = await _service.GetMenuItemAsync(id);

        Assert.NotNull(item);

        return item.Code;
    }

    //================================================== Making a code ==================================================//

    [Fact]
    public async Task Create_FirstDishInACategory_GetsTheCategoryLetterAndNumberOne()
    {
        // Act
        int id = await AddDishAsync("Smoky BBQ Burger", GrillsId);

        // Assert: Grills starts with G, and it is the first G dish.
        Assert.Equal("G01", await CodeOfAsync(id));
    }

    [Fact]
    public async Task Create_SecondDishInTheSameCategory_GetsTheNextNumber()
    {
        // Act
        int first = await AddDishAsync("Smoky BBQ Burger", GrillsId);
        int second = await AddDishAsync("Lamb Chops", GrillsId);

        // Assert
        Assert.Equal("G01", await CodeOfAsync(first));
        Assert.Equal("G02", await CodeOfAsync(second));
    }

    [Fact]
    public async Task Create_DishesInDifferentCategories_UseTheirOwnLetters()
    {
        // Act
        int starter = await AddDishAsync("Mushroom Soup", StartersId);
        int pasta = await AddDishAsync("Bacon Carbonara", PastaId);
        int drink = await AddDishAsync("Orange Juice", BeveragesId);

        // Assert: each category counts on its own.
        Assert.Equal("S01", await CodeOfAsync(starter));
        Assert.Equal("P01", await CodeOfAsync(pasta));
        Assert.Equal("B01", await CodeOfAsync(drink));
    }

    [Fact]
    public async Task Create_CategoriesThatShareALetter_ShareOneNumbering()
    {
        // Arrange: rename the seeded Desserts category (Id 4) to "Sides", through WL's service like an
        // administrator would, so two categories now start with S.
        const int DessertsId = 4;
        await _restaurantService.UpdateMenuCategoryAsync(
            DessertsId,
            new MenuCategoryInput { Name = "Sides", DisplayOrder = 40, IsActive = true });

        // Act
        int starter = await AddDishAsync("Mushroom Soup", StartersId);
        int side = await AddDishAsync("Fries", DessertsId);

        // Assert: the two codes differ, so a code stays unique on the whole menu.
        Assert.Equal("S01", await CodeOfAsync(starter));
        Assert.Equal("S02", await CodeOfAsync(side));
    }

    [Fact]
    public async Task Create_WhenTheHighestNumberIsNinetyNine_GoesOnToAHundred()
    {
        // Arrange: write a dish with code G99 straight to the database.
        DateTime now = DateTime.UtcNow;
        _context.MenuItems.Add(new MenuItem
        {
            Code = "G99",
            Name = "Old Grill",
            MenuCategoryId = GrillsId,
            Price = NormalPrice,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await _context.SaveChangesAsync();

        // Act
        int id = await AddDishAsync("New Grill", GrillsId);

        // Assert: the number grows instead of wrapping around.
        Assert.Equal("G100", await CodeOfAsync(id));
    }

    //================================================== A code never changes ==================================================//

    [Fact]
    public async Task Update_RenamingAndMovingTheDish_KeepsItsCode()
    {
        // Arrange
        int id = await AddDishAsync("Smoky BBQ Burger", GrillsId);

        MenuItemInput changed = new MenuItemInput
        {
            Name = "Renamed Pasta Dish",
            Description = "Plain description",
            MenuCategoryId = PastaId,
            Price = NormalPrice,
            IsAvailable = true
        };

        // Act
        ServiceResult result = await _service.UpdateMenuItemAsync(id, changed);

        // Assert: still the code it got when it was created.
        Assert.True(result.Succeeded);
        Assert.Equal("G01", await CodeOfAsync(id));
    }

    //================================================== Showing and searching the code ==================================================//

    [Fact]
    public async Task Search_ByCode_FindsTheDish_IgnoringCase()
    {
        // Arrange
        await AddDishAsync("Smoky BBQ Burger", GrillsId);
        await AddDishAsync("Bacon Carbonara", PastaId);

        // Act
        IReadOnlyList<MenuItemCard> cards = await _service.SearchMenuAsync(
            new MenuSearchCriteria { SearchText = "p01" });

        // Assert
        MenuItemCard card = Assert.Single(cards);
        Assert.Equal("Bacon Carbonara", card.Name);
    }

    [Fact]
    public async Task Search_Cards_CarryTheCode()
    {
        // Arrange
        await AddDishAsync("Smoky BBQ Burger", GrillsId);

        // Act
        IReadOnlyList<MenuItemCard> cards = await _service.SearchMenuAsync(new MenuSearchCriteria());

        // Assert: the card the pages show has the code.
        MenuItemCard card = Assert.Single(cards);
        Assert.Equal("G01", card.Code);
    }

    //================================================== The database is the last line of defence ==================================================//

    [Fact]
    public async Task Database_DuplicateCodeThatBypassesTheService_IsBlockedByTheUniqueIndex()
    {
        // Arrange: skip the service and write two dishes with the same code straight to the database.
        DateTime now = DateTime.UtcNow;
        _context.MenuItems.Add(new MenuItem { Code = "G01", Name = "One", MenuCategoryId = GrillsId, Price = NormalPrice, CreatedAtUtc = now, UpdatedAtUtc = now });
        _context.MenuItems.Add(new MenuItem { Code = "G01", Name = "Two", MenuCategoryId = GrillsId, Price = NormalPrice, CreatedAtUtc = now, UpdatedAtUtc = now });

        // Act + Assert
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_SeveralDishesWithoutACode_AreAllowed()
    {
        // Arrange: dishes saved before codes existed have no code, and must not clash with each other.
        DateTime now = DateTime.UtcNow;
        _context.MenuItems.Add(new MenuItem { Name = "Old One", MenuCategoryId = GrillsId, Price = NormalPrice, CreatedAtUtc = now, UpdatedAtUtc = now });
        _context.MenuItems.Add(new MenuItem { Name = "Old Two", MenuCategoryId = GrillsId, Price = NormalPrice, CreatedAtUtc = now, UpdatedAtUtc = now });

        // Act
        int saved = await _context.SaveChangesAsync();

        // Assert
        Assert.Equal(2, saved);
    }

    /// <summary>A stand-in for the photo storage: these tests never use photos.</summary>
    private class StubImageStorage : IMenuImageStorage
    {
        public Task<ServiceResult<string>> SaveAsync(Stream content, string originalFileName)
        {
            return Task.FromResult(ServiceResult<string>.Success("photo.png"));
        }

        public void Delete(string fileName)
        {
        }

        public string GetPublicUrl(string fileName)
        {
            return "/uploads/menu/" + fileName;
        }
    }
}
