using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Rroms.Web.Common;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Tests.Menu;

/// <summary>
/// Unit tests for the YJ-1 menu item rules in <see cref="MenuService"/>.
/// </summary>
/// <remarks>
/// Every test follows Arrange (prepare data) - Act (call the method) - Assert (check the result).
/// Seed data available in every test: five menu categories, Starters (Id 1), Grills (Id 2),
/// Pasta (Id 3), Desserts (Id 4) and Beverages (Id 5). No menu items are seeded.
/// Prices in the offer tests use the same number of digits (20.00 and 15.00): SQLite stores
/// decimals as text, so its check constraint compares them as text.
/// </remarks>
public class MenuServiceTests : IDisposable
{
    /// <summary>Id of the seeded "Starters" category.</summary>
    private const int StartersId = 1;

    /// <summary>Id of the seeded "Grills" category.</summary>
    private const int GrillsId = 2;

    /// <summary>Id of the seeded "Beverages" category.</summary>
    private const int BeveragesId = 5;

    /// <summary>A category Id that does not exist.</summary>
    private const int UnknownCategoryId = 999;

    /// <summary>A menu item Id that does not exist.</summary>
    private const int UnknownItemId = 999;

    private const decimal NormalPrice = 20.00m;
    private const decimal OfferPrice = 15.00m;

    private static readonly DateTime OfferStart = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OfferEnd = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly RestaurantService _restaurantService;
    private readonly FakeMenuImageStorage _imageStorage;
    private readonly MenuService _service;

    /// <summary>
    /// Runs before EVERY test: creates a brand-new in-memory database, so tests never affect
    /// each other and never touch the real SQL Server database.
    /// </summary>
    public MenuServiceTests()
    {
        // The in-memory SQLite database lives only while this connection is open.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);

        // Builds every table from the code, including WL's seed data and my check constraints.
        _context.Database.EnsureCreated();

        _restaurantService = new RestaurantService(_context);
        _imageStorage = new FakeMenuImageStorage();
        _service = new MenuService(_context, _restaurantService, _imageStorage);
    }

    /// <summary>Runs after EVERY test: closing the connection deletes the database.</summary>
    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>Builds a valid item so each test only changes the one thing it is about.</summary>
    private static MenuItemInput CreateValidInput(string name = "Smoky BBQ Burger", int categoryId = GrillsId)
    {
        var input = new MenuItemInput
        {
            Name = name,
            Description = "Beef patty with smoky sauce",
            MenuCategoryId = categoryId,
            Price = NormalPrice,
            IsAvailable = true
        };

        return input;
    }

    //================================================== Create ==================================================//

    [Fact]
    public async Task CreateMenuItem_WithValidInput_SavesTheItem()
    {
        // Arrange
        MenuItemInput input = CreateValidInput();

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        // Assert: saved, with an Id from the database and the values we sent.
        Assert.True(result.Succeeded);
        Assert.True(result.Value > 0);

        MenuItem? saved = await _service.GetMenuItemAsync(result.Value);
        Assert.NotNull(saved);
        Assert.Equal("Smoky BBQ Burger", saved.Name);
        Assert.Equal(NormalPrice, saved.Price);
        Assert.Equal(GrillsId, saved.MenuCategoryId);
        Assert.True(saved.IsAvailable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task CreateMenuItem_WithPriceZeroOrBelow_IsRejected(int price)
    {
        // Arrange: runs twice, once with 0 and once with -5.
        MenuItemInput input = CreateValidInput();
        input.Price = price;

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.PriceMessage, result.Errors);
    }

    [Fact]
    public async Task CreateMenuItem_WithEmptyName_IsRejected()
    {
        // Arrange: spaces only must count as empty.
        MenuItemInput input = CreateValidInput(name: "   ");

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.NameRequiredMessage, result.Errors);
    }

    [Fact]
    public async Task CreateMenuItem_WithUnknownCategory_IsRejected()
    {
        // Arrange
        MenuItemInput input = CreateValidInput(categoryId: UnknownCategoryId);

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.CategoryNotFoundMessage, result.Errors);
    }

    [Fact]
    public async Task CreateMenuItem_InInactiveCategory_IsRejected()
    {
        // Arrange: switch Beverages off, then try to add a drink to it.
        await DeactivateCategoryAsync(BeveragesId, "Beverages", 50);
        MenuItemInput input = CreateValidInput(name: "Iced Lemon Tea", categoryId: BeveragesId);

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.InactiveCategoryMessage, result.Errors);
    }

    [Fact]
    public async Task CreateMenuItem_DuplicateNameInSameCategoryDifferentCase_IsRejected()
    {
        // Arrange: "Smoky BBQ Burger" exists; "smoky bbq burger" must count as the same dish.
        await _service.CreateMenuItemAsync(CreateValidInput());
        MenuItemInput duplicate = CreateValidInput(name: "smoky bbq burger");

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(duplicate);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.DuplicateNameMessage, result.Errors);
    }

    [Fact]
    public async Task CreateMenuItem_SameNameInDifferentCategory_Succeeds()
    {
        // Arrange: a "House Special" starter and a "House Special" grill are different dishes.
        await _service.CreateMenuItemAsync(CreateValidInput(name: "House Special", categoryId: StartersId));
        MenuItemInput grill = CreateValidInput(name: "House Special", categoryId: GrillsId);

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(grill);

        // Assert
        Assert.True(result.Succeeded);
    }

    //================================================== Offers ==================================================//

    [Fact]
    public async Task CreateMenuItem_WithCompleteOffer_SavesTheOffer()
    {
        // Arrange
        MenuItemInput input = CreateValidInput();
        input.PromoPrice = OfferPrice;
        input.PromoStartUtc = OfferStart;
        input.PromoEndUtc = OfferEnd;

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        // Assert
        Assert.True(result.Succeeded);
        MenuItem? saved = await _service.GetMenuItemAsync(result.Value);
        Assert.NotNull(saved);
        Assert.Equal(OfferPrice, saved.PromoPrice);
        Assert.Equal(OfferStart, saved.PromoStartUtc);
        Assert.Equal(OfferEnd, saved.PromoEndUtc);
    }

    [Fact]
    public async Task CreateMenuItem_OfferPriceWithoutDates_IsRejected()
    {
        // Arrange: a price but no start or end time.
        MenuItemInput input = CreateValidInput();
        input.PromoPrice = OfferPrice;

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.PromoIncompleteMessage, result.Errors);
    }

    [Fact]
    public async Task CreateMenuItem_OfferNotCheaperThanNormalPrice_IsRejected()
    {
        // Arrange: an "offer" of RM 25 on a RM 20 dish makes no sense.
        MenuItemInput input = CreateValidInput();
        input.PromoPrice = 25.00m;
        input.PromoStartUtc = OfferStart;
        input.PromoEndUtc = OfferEnd;

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.PromoBelowPriceMessage, result.Errors);
    }

    [Fact]
    public async Task CreateMenuItem_OfferEndingBeforeItStarts_IsRejected()
    {
        // Arrange: start and end swapped.
        MenuItemInput input = CreateValidInput();
        input.PromoPrice = OfferPrice;
        input.PromoStartUtc = OfferEnd;
        input.PromoEndUtc = OfferStart;

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.PromoDatesMessage, result.Errors);
    }

    //================================================== Update ==================================================//

    [Fact]
    public async Task UpdateMenuItem_ChangesThePrice()
    {
        // Arrange
        ServiceResult<int> created = await _service.CreateMenuItemAsync(CreateValidInput());
        MenuItemInput edit = CreateValidInput();
        edit.Price = 22.50m;

        // Act
        ServiceResult result = await _service.UpdateMenuItemAsync(created.Value, edit);

        // Assert
        Assert.True(result.Succeeded);
        MenuItem? saved = await _service.GetMenuItemAsync(created.Value);
        Assert.NotNull(saved);
        Assert.Equal(22.50m, saved.Price);
    }

    [Fact]
    public async Task UpdateMenuItem_KeepingItsOwnName_Succeeds()
    {
        // Arrange: edit only the description. Its own name must not count as a duplicate.
        ServiceResult<int> created = await _service.CreateMenuItemAsync(CreateValidInput());
        MenuItemInput edit = CreateValidInput();
        edit.Description = "Now with extra cheese";

        // Act
        ServiceResult result = await _service.UpdateMenuItemAsync(created.Value, edit);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task UpdateMenuItem_KeepingACategorySwitchedOffSinceCreation_Succeeds()
    {
        // Arrange: the item exists, then its category is switched off. Editing the price must still work.
        ServiceResult<int> created = await _service.CreateMenuItemAsync(
            CreateValidInput(name: "Iced Lemon Tea", categoryId: BeveragesId));
        await DeactivateCategoryAsync(BeveragesId, "Beverages", 50);

        MenuItemInput edit = CreateValidInput(name: "Iced Lemon Tea", categoryId: BeveragesId);
        edit.Price = 6.00m;

        // Act
        ServiceResult result = await _service.UpdateMenuItemAsync(created.Value, edit);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task UpdateMenuItem_UnknownId_IsRejected()
    {
        // Act
        ServiceResult result = await _service.UpdateMenuItemAsync(UnknownItemId, CreateValidInput());

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.ItemNotFoundMessage, result.Errors);
    }

    //================================================== Availability, delete and lists ==================================================//

    [Fact]
    public async Task SetAvailability_TurnsOrderingOffAndOn()
    {
        // Arrange
        ServiceResult<int> created = await _service.CreateMenuItemAsync(CreateValidInput());

        // Act + Assert: off.
        await _service.SetAvailabilityAsync(created.Value, false);
        MenuItem? whenOff = await _service.GetMenuItemAsync(created.Value);
        Assert.NotNull(whenOff);
        Assert.False(whenOff.IsAvailable);

        // Act + Assert: back on.
        await _service.SetAvailabilityAsync(created.Value, true);
        MenuItem? whenOn = await _service.GetMenuItemAsync(created.Value);
        Assert.NotNull(whenOn);
        Assert.True(whenOn.IsAvailable);
    }

    [Fact]
    public async Task DeleteMenuItem_RemovesTheItem()
    {
        // Arrange
        ServiceResult<int> created = await _service.CreateMenuItemAsync(CreateValidInput());

        // Act
        ServiceResult result = await _service.DeleteMenuItemAsync(created.Value);

        // Assert
        Assert.True(result.Succeeded);
        MenuItem? gone = await _service.GetMenuItemAsync(created.Value);
        Assert.Null(gone);
    }

    [Fact]
    public async Task GetMenuItems_FilteredByCategory_ReturnsOnlyThatCategory()
    {
        // Arrange: one starter and one grill.
        await _service.CreateMenuItemAsync(CreateValidInput(name: "Garlic Bread", categoryId: StartersId));
        await _service.CreateMenuItemAsync(CreateValidInput(name: "Smoky BBQ Burger", categoryId: GrillsId));

        // Act
        IReadOnlyList<MenuItem> grills = await _service.GetMenuItemsAsync(GrillsId);
        IReadOnlyList<MenuItem> everything = await _service.GetMenuItemsAsync(null);

        // Assert
        Assert.Single(grills);
        Assert.Equal("Smoky BBQ Burger", grills[0].Name);
        Assert.Equal(2, everything.Count);
    }

    //================================================== Photos ==================================================//

    [Fact]
    public async Task SetImage_WithAcceptedPhoto_LinksItToTheItem()
    {
        // Arrange
        ServiceResult<int> created = await _service.CreateMenuItemAsync(CreateValidInput());
        using MemoryStream photo = new MemoryStream(new byte[] { 1 });

        // Act
        ServiceResult result = await _service.SetImageAsync(created.Value, photo, "burger.png");

        // Assert
        Assert.True(result.Succeeded);
        MenuItem? saved = await _service.GetMenuItemAsync(created.Value);
        Assert.NotNull(saved);
        Assert.Equal("photo1.png", saved.ImageFileName);
    }

    [Fact]
    public async Task SetImage_ReplacingAPhoto_DeletesTheOldFile()
    {
        // Arrange: the item already has photo1.png.
        ServiceResult<int> created = await _service.CreateMenuItemAsync(CreateValidInput());
        using MemoryStream first = new MemoryStream(new byte[] { 1 });
        await _service.SetImageAsync(created.Value, first, "first.png");
        using MemoryStream second = new MemoryStream(new byte[] { 2 });

        // Act
        ServiceResult result = await _service.SetImageAsync(created.Value, second, "second.png");

        // Assert: the item now uses photo2.png and the old file was removed from disk.
        Assert.True(result.Succeeded);
        MenuItem? saved = await _service.GetMenuItemAsync(created.Value);
        Assert.NotNull(saved);
        Assert.Equal("photo2.png", saved.ImageFileName);
        Assert.Contains("photo1.png", _imageStorage.DeletedFiles);
        Assert.DoesNotContain("photo2.png", _imageStorage.DeletedFiles);
    }

    [Fact]
    public async Task SetImage_WhenThePhotoIsRejected_KeepsTheOldPhoto()
    {
        // Arrange: the item has a photo, then the storage starts refusing uploads.
        ServiceResult<int> created = await _service.CreateMenuItemAsync(CreateValidInput());
        using MemoryStream first = new MemoryStream(new byte[] { 1 });
        await _service.SetImageAsync(created.Value, first, "first.png");

        _imageStorage.ErrorToReturn = MenuImageStorage.NotAnImageMessage;
        using MemoryStream bad = new MemoryStream(new byte[] { 2 });

        // Act
        ServiceResult result = await _service.SetImageAsync(created.Value, bad, "bad.png");

        // Assert: the storage's message is passed on, and nothing was changed or deleted.
        Assert.False(result.Succeeded);
        Assert.Contains(MenuImageStorage.NotAnImageMessage, result.Errors);
        MenuItem? saved = await _service.GetMenuItemAsync(created.Value);
        Assert.NotNull(saved);
        Assert.Equal("photo1.png", saved.ImageFileName);
        Assert.Empty(_imageStorage.DeletedFiles);
    }

    [Fact]
    public async Task SetImage_UnknownItem_IsRejected()
    {
        // Arrange
        using MemoryStream photo = new MemoryStream(new byte[] { 1 });

        // Act
        ServiceResult result = await _service.SetImageAsync(UnknownItemId, photo, "burger.png");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.ItemNotFoundMessage, result.Errors);
    }

    [Fact]
    public async Task RemoveImage_ClearsTheLinkAndDeletesTheFile()
    {
        // Arrange
        ServiceResult<int> created = await _service.CreateMenuItemAsync(CreateValidInput());
        using MemoryStream photo = new MemoryStream(new byte[] { 1 });
        await _service.SetImageAsync(created.Value, photo, "burger.png");

        // Act
        ServiceResult result = await _service.RemoveImageAsync(created.Value);

        // Assert
        Assert.True(result.Succeeded);
        MenuItem? saved = await _service.GetMenuItemAsync(created.Value);
        Assert.NotNull(saved);
        Assert.Null(saved.ImageFileName);
        Assert.Contains("photo1.png", _imageStorage.DeletedFiles);
    }

    [Fact]
    public async Task DeleteMenuItem_AlsoDeletesItsPhoto()
    {
        // Arrange
        ServiceResult<int> created = await _service.CreateMenuItemAsync(CreateValidInput());
        using MemoryStream photo = new MemoryStream(new byte[] { 1 });
        await _service.SetImageAsync(created.Value, photo, "burger.png");

        // Act
        await _service.DeleteMenuItemAsync(created.Value);

        // Assert
        Assert.Contains("photo1.png", _imageStorage.DeletedFiles);
    }

    /// <summary>Switches a seeded category off through WL's service, like an administrator would.</summary>
    private async Task DeactivateCategoryAsync(int id, string name, int displayOrder)
    {
        var input = new MenuCategoryInput
        {
            Name = name,
            DisplayOrder = displayOrder,
            IsActive = false
        };

        await _restaurantService.UpdateMenuCategoryAsync(id, input);
    }
    /// <summary>
    /// A stand-in for the real photo storage: it accepts everything (unless told to refuse), hands out the names
    /// photo1.png, photo2.png, ... and records which files were deleted, so these tests need no real images.
    /// </summary>
    private class FakeMenuImageStorage : IMenuImageStorage
    {
        private int _savedCount;

        /// <summary>When set, every save is refused with this message.</summary>
        public string? ErrorToReturn { get; set; }

        /// <summary>The file names the service asked to delete.</summary>
        public List<string> DeletedFiles { get; } = new List<string>();

        public Task<ServiceResult<string>> SaveAsync(Stream content, string originalFileName)
        {
            if (ErrorToReturn != null)
            {
                return Task.FromResult(ServiceResult<string>.Failure(ErrorToReturn));
            }

            _savedCount++;
            return Task.FromResult(ServiceResult<string>.Success($"photo{_savedCount}.png"));
        }

        public void Delete(string fileName)
        {
            DeletedFiles.Add(fileName);
        }

        public string GetPublicUrl(string fileName)
        {
            return "/uploads/menu/" + fileName;
        }
    }
}