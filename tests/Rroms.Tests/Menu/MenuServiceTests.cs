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

    /// <summary>Offers cannot start in the past any more, so test offers start tomorrow and end a week later.</summary>
    private readonly DateTime _offerStart = DateTime.UtcNow.AddDays(1);
    private readonly DateTime _offerEnd = DateTime.UtcNow.AddDays(8);

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
    public async Task CreateMenuItem_SameNameInDifferentCategory_IsRejected()
    {
        // Arrange: "Tiramisu" is a dessert; the same name as a starter is still the same dish name.
        await _service.CreateMenuItemAsync(CreateValidInput(name: "Tiramisu", categoryId: 4));
        MenuItemInput starter = CreateValidInput(name: "tiramisu", categoryId: StartersId);

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(starter);

        // Assert: refused whatever the category or capital letters.
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.DuplicateNameMessage, result.Errors);
    }

    [Fact]
    public async Task UpdateMenuItem_RenamedToANameUsedInAnotherCategory_IsRejected()
    {
        // Arrange: two different dishes in two categories, then the second is renamed like the first.
        await _service.CreateMenuItemAsync(CreateValidInput(name: "Tiramisu", categoryId: 4));
        ServiceResult<int> created = await _service.CreateMenuItemAsync(CreateValidInput(name: "Garlic Bread", categoryId: StartersId));
        MenuItemInput rename = CreateValidInput(name: "Tiramisu", categoryId: StartersId);

        // Act
        ServiceResult result = await _service.UpdateMenuItemAsync(created.Value, rename);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.DuplicateNameMessage, result.Errors);
    }

    [Fact]
    public async Task Database_DuplicateNameThatBypassesTheService_IsBlockedByTheUniqueIndex()
    {
        // Arrange: skip the service and write straight to the database, like a bug or two administrators
        // saving at the same moment. Two dishes in different categories with the same name.
        DateTime now = DateTime.UtcNow;
        _context.MenuItems.Add(new MenuItem { Name = "Tiramisu", MenuCategoryId = 4, Price = 12.00m, CreatedAtUtc = now, UpdatedAtUtc = now });
        _context.MenuItems.Add(new MenuItem { Name = "Tiramisu", MenuCategoryId = StartersId, Price = 12.00m, CreatedAtUtc = now, UpdatedAtUtc = now });

        // Act + Assert: the unique index on Name refuses the second one.
        await Assert.ThrowsAsync<DbUpdateException>(() => _context.SaveChangesAsync());
    }

    //================================================== Offers cannot be back-dated ==================================================//

    [Fact]
    public async Task CreateMenuItem_OfferStartingInThePast_IsRejected()
    {
        // Arrange: the offer is said to have started two days ago.
        MenuItemInput input = CreateValidInput();
        input.PromoPrice = OfferPrice;
        input.PromoStartUtc = DateTime.UtcNow.AddDays(-2);
        input.PromoEndUtc = DateTime.UtcNow.AddDays(1);

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.PromoInPastMessage, result.Errors);
    }

    [Fact]
    public async Task CreateMenuItem_OfferEndingInThePast_IsRejected()
    {
        // Arrange: an offer that is already over.
        MenuItemInput input = CreateValidInput();
        input.PromoPrice = OfferPrice;
        input.PromoStartUtc = DateTime.UtcNow.AddDays(-3);
        input.PromoEndUtc = DateTime.UtcNow.AddDays(-1);

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.PromoInPastMessage, result.Errors);
    }

    [Fact]
    public async Task CreateMenuItem_OfferStartingNow_IsAccepted()
    {
        // Arrange: "start now" arrives a few seconds old, which the small grace period allows.
        MenuItemInput input = CreateValidInput();
        input.PromoPrice = OfferPrice;
        input.PromoStartUtc = DateTime.UtcNow.AddSeconds(-30);
        input.PromoEndUtc = DateTime.UtcNow.AddDays(1);

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task UpdateMenuItem_RunningOfferLeftUnchanged_CanStillBeSaved()
    {
        // Arrange: an offer that started yesterday is running. Only the normal price is changed.
        int id = await AddDishWithOfferAsync("Offer Burger", startOffsetDays: -1, endOffsetDays: 1);
        MenuItem dish = await _context.MenuItems.SingleAsync(item => item.Id == id);

        MenuItemInput edit = CreateValidInput("Offer Burger");
        edit.Price = 22.00m;
        edit.PromoPrice = dish.PromoPrice;
        edit.PromoStartUtc = dish.PromoStartUtc;
        edit.PromoEndUtc = dish.PromoEndUtc;

        // Act
        ServiceResult result = await _service.UpdateMenuItemAsync(id, edit);

        // Assert: the start is in the past, but it was not changed, so it is not checked again.
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task UpdateMenuItem_MovingTheOfferEndIntoThePast_IsRejected()
    {
        // Arrange: the running offer is given an end time of one hour ago.
        int id = await AddDishWithOfferAsync("Offer Burger", startOffsetDays: -1, endOffsetDays: 1);
        MenuItem dish = await _context.MenuItems.SingleAsync(item => item.Id == id);

        MenuItemInput edit = CreateValidInput("Offer Burger");
        edit.PromoPrice = dish.PromoPrice;
        edit.PromoStartUtc = dish.PromoStartUtc;
        edit.PromoEndUtc = DateTime.UtcNow.AddHours(-1);

        // Act
        ServiceResult result = await _service.UpdateMenuItemAsync(id, edit);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.PromoInPastMessage, result.Errors);
    }

    //================================================== Offers ==================================================//

    [Fact]
    public async Task CreateMenuItem_WithCompleteOffer_SavesTheOffer()
    {
        // Arrange
        MenuItemInput input = CreateValidInput();
        input.PromoPrice = OfferPrice;
        input.PromoStartUtc = _offerStart;
        input.PromoEndUtc = _offerEnd;

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        // Assert
        Assert.True(result.Succeeded);
        MenuItem? saved = await _service.GetMenuItemAsync(result.Value);
        Assert.NotNull(saved);
        Assert.Equal(OfferPrice, saved.PromoPrice);
        Assert.Equal(_offerStart, saved.PromoStartUtc);
        Assert.Equal(_offerEnd, saved.PromoEndUtc);
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
        input.PromoStartUtc = _offerStart;
        input.PromoEndUtc = _offerEnd;

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
        input.PromoStartUtc = _offerEnd;
        input.PromoEndUtc = _offerStart;

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

    [Fact]
    public async Task DeleteMenuItem_ThatHasBeenOrdered_IsRejectedAndKept()
    {
        // Arrange: a customer has ordered the dish.
        ServiceResult<int> created = await _service.CreateMenuItemAsync(CreateValidInput());
        await OrderTestData.AddOrderAsync(_context, created.Value);

        // Act
        ServiceResult result = await _service.DeleteMenuItemAsync(created.Value);

        // Assert: refused with a helpful message, and the dish is still there.
        Assert.False(result.Succeeded);
        Assert.Contains(MenuService.ItemHasOrdersMessage, result.Errors);
        MenuItem? stillThere = await _service.GetMenuItemAsync(created.Value);
        Assert.NotNull(stillThere);
    }

    //================================================== Customer menu (YJ-2) ==================================================//

    /// <summary>Creates a dish for the menu tests and returns its Id.</summary>
    private async Task<int> AddDishAsync(
        string name,
        int categoryId = GrillsId,
        decimal price = NormalPrice,
        bool isAvailable = true,
        string description = "Plain description")
    {
        MenuItemInput input = CreateValidInput(name, categoryId);
        input.Price = price;
        input.IsAvailable = isAvailable;
        input.Description = description;

        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);
        Assert.True(result.Succeeded);

        return result.Value;
    }

    /// <summary>
    /// Creates a dish with an offer from <paramref name="startOffsetDays"/> to <paramref name="endOffsetDays"/> days from now.
    /// The offer is written straight into the database: the service refuses back-dated offers, but the menu must still
    /// handle one that started yesterday (a running offer) or ended yesterday (an expired one).
    /// </summary>
    private async Task<int> AddDishWithOfferAsync(string name, int startOffsetDays, int endOffsetDays)
    {
        DateTime now = DateTime.UtcNow;

        ServiceResult<int> result = await _service.CreateMenuItemAsync(CreateValidInput(name));
        Assert.True(result.Succeeded);

        MenuItem dish = await _context.MenuItems.SingleAsync(item => item.Id == result.Value);
        dish.PromoPrice = OfferPrice;
        dish.PromoStartUtc = now.AddDays(startOffsetDays);
        dish.PromoEndUtc = now.AddDays(endOffsetDays);
        await _context.SaveChangesAsync();

        return result.Value;
    }

    [Fact]
    public async Task SearchMenu_NoFilters_ListsDishesByCategoryOrderThenName()
    {
        // Arrange: Starters come before Grills, then Desserts (the seeded display order 10, 20, 40).
        await AddDishAsync("Zebra Steak", GrillsId);
        await AddDishAsync("Apple Pie", 4);
        await AddDishAsync("Garlic Bread", StartersId);
        await AddDishAsync("Burger", GrillsId);

        // Act
        IReadOnlyList<MenuItemCard> cards = await _service.SearchMenuAsync(new MenuSearchCriteria());

        // Assert
        List<string> names = cards.Select(card => card.Name).ToList();
        Assert.Equal(new List<string> { "Garlic Bread", "Burger", "Zebra Steak", "Apple Pie" }, names);
    }

    [Fact]
    public async Task SearchMenu_ByText_MatchesNameOrDescriptionIgnoringCase()
    {
        // Arrange: one match in the name, one in the description, one with no match.
        await AddDishAsync("Smoky BBQ Burger", description: "Beef patty");
        await AddDishAsync("Ribs", description: "Served with SMOKY sauce");
        await AddDishAsync("Garden Salad", description: "Fresh and light");

        // Act
        IReadOnlyList<MenuItemCard> cards = await _service.SearchMenuAsync(
            new MenuSearchCriteria { SearchText = "smoky" });

        // Assert
        List<string> names = cards.Select(card => card.Name).OrderBy(name => name).ToList();
        Assert.Equal(new List<string> { "Ribs", "Smoky BBQ Burger" }, names);
    }

    [Fact]
    public async Task SearchMenu_ByCategory_ReturnsOnlyThatCategory()
    {
        // Arrange
        await AddDishAsync("Garlic Bread", StartersId);
        await AddDishAsync("Burger", GrillsId);

        // Act
        IReadOnlyList<MenuItemCard> cards = await _service.SearchMenuAsync(
            new MenuSearchCriteria { MenuCategoryId = GrillsId });

        // Assert
        Assert.Single(cards);
        Assert.Equal("Burger", cards[0].Name);
    }

    [Fact]
    public async Task SearchMenu_DishInAHiddenCategory_IsNotShown()
    {
        // Arrange: switch Beverages off after adding a drink to it.
        await AddDishAsync("Iced Lemon Tea", BeveragesId);
        await DeactivateCategoryAsync(BeveragesId, "Beverages", 50);

        // Act
        IReadOnlyList<MenuItemCard> cards = await _service.SearchMenuAsync(new MenuSearchCriteria());

        // Assert
        Assert.Empty(cards);
    }

    [Fact]
    public async Task SearchMenu_PriceFilter_UsesThePriceTheCustomerWouldPayToday()
    {
        // Arrange: a RM 20 dish with a RM 15 offer running now, and a plain RM 20 dish.
        await AddDishWithOfferAsync("Offer Burger", startOffsetDays: -1, endOffsetDays: 1);
        await AddDishAsync("Plain Burger");

        // Act: nothing above RM 16.
        IReadOnlyList<MenuItemCard> cards = await _service.SearchMenuAsync(
            new MenuSearchCriteria { MaxPrice = 16.00m });

        // Assert: only the dish whose offer price fits.
        Assert.Single(cards);
        Assert.Equal("Offer Burger", cards[0].Name);
    }

    [Fact]
    public async Task SearchMenu_MinimumPrice_LeavesOutCheaperDishes()
    {
        // Arrange
        await AddDishAsync("Cheap Bites", StartersId, price: 8.00m);
        await AddDishAsync("Big Steak", GrillsId, price: 45.00m);

        // Act
        IReadOnlyList<MenuItemCard> cards = await _service.SearchMenuAsync(
            new MenuSearchCriteria { MinPrice = 10.00m });

        // Assert
        Assert.Single(cards);
        Assert.Equal("Big Steak", cards[0].Name);
    }

    [Fact]
    public async Task SearchMenu_RunningOffer_ShowsOfferPriceAndEndTime()
    {
        // Arrange
        await AddDishWithOfferAsync("Offer Burger", startOffsetDays: -1, endOffsetDays: 1);

        // Act
        IReadOnlyList<MenuItemCard> cards = await _service.SearchMenuAsync(new MenuSearchCriteria());

        // Assert
        MenuItemCard card = Assert.Single(cards);
        Assert.True(card.IsOfferActive);
        Assert.Equal(NormalPrice, card.NormalPrice);
        Assert.Equal(OfferPrice, card.CurrentPrice);
        Assert.NotNull(card.OfferEndsAtUtc);
    }

    [Fact]
    public async Task SearchMenu_ExpiredOffer_ShowsTheNormalPrice()
    {
        // Arrange: the offer ended yesterday.
        await AddDishWithOfferAsync("Old Offer Burger", startOffsetDays: -2, endOffsetDays: -1);

        // Act
        IReadOnlyList<MenuItemCard> cards = await _service.SearchMenuAsync(new MenuSearchCriteria());

        // Assert
        MenuItemCard card = Assert.Single(cards);
        Assert.False(card.IsOfferActive);
        Assert.Equal(NormalPrice, card.CurrentPrice);
        Assert.Null(card.OfferEndsAtUtc);
    }

    [Fact]
    public async Task SearchMenu_UnavailableDish_IsListedButCannotBeOrdered()
    {
        // Arrange
        await AddDishAsync("Sold Out Special", isAvailable: false);
        await AddDishAsync("Normal Burger", isAvailable: true);

        // Act
        IReadOnlyList<MenuItemCard> cards = await _service.SearchMenuAsync(new MenuSearchCriteria());

        // Assert: both are listed, but only the available one can be ordered.
        Assert.Equal(2, cards.Count);
        Assert.False(cards.Single(card => card.Name == "Sold Out Special").CanOrder);
        Assert.True(cards.Single(card => card.Name == "Normal Burger").CanOrder);
    }

    [Fact]
    public async Task SearchMenu_DishWithAPhoto_CarriesThePhotoAddress()
    {
        // Arrange
        int id = await AddDishAsync("Photo Burger");
        using MemoryStream photo = new MemoryStream(new byte[] { 1 });
        await _service.SetImageAsync(id, photo, "burger.png");

        // Act
        IReadOnlyList<MenuItemCard> cards = await _service.SearchMenuAsync(new MenuSearchCriteria());

        // Assert
        MenuItemCard card = Assert.Single(cards);
        Assert.Equal("/uploads/menu/photo1.png", card.PhotoUrl);
    }

    //================================================== Dietary tags ==================================================//

    [Fact]
    public async Task CreateMenuItem_WithDietaryTags_SavesThem()
    {
        // Arrange
        MenuItemInput input = CreateValidInput("Veggie Burger");
        input.IsVegetarian = true;
        input.IsSpicy = true;

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        // Assert
        Assert.True(result.Succeeded);
        MenuItem? saved = await _service.GetMenuItemAsync(result.Value);
        Assert.NotNull(saved);
        Assert.True(saved.IsVegetarian);
        Assert.True(saved.IsSpicy);
    }

    [Fact]
    public async Task CreateMenuItem_WithoutDietaryTags_DefaultsToNeither()
    {
        // Arrange
        MenuItemInput input = CreateValidInput("Plain Burger");

        // Act
        ServiceResult<int> result = await _service.CreateMenuItemAsync(input);

        // Assert
        MenuItem? saved = await _service.GetMenuItemAsync(result.Value);
        Assert.NotNull(saved);
        Assert.False(saved.IsVegetarian);
        Assert.False(saved.IsSpicy);
    }

    [Fact]
    public async Task UpdateMenuItem_ChangesTheDietaryTags()
    {
        // Arrange: starts without tags.
        ServiceResult<int> created = await _service.CreateMenuItemAsync(CreateValidInput("Burger"));
        MenuItemInput edit = CreateValidInput("Burger");
        edit.IsSpicy = true;

        // Act
        ServiceResult result = await _service.UpdateMenuItemAsync(created.Value, edit);

        // Assert
        Assert.True(result.Succeeded);
        MenuItem? saved = await _service.GetMenuItemAsync(created.Value);
        Assert.NotNull(saved);
        Assert.True(saved.IsSpicy);
        Assert.False(saved.IsVegetarian);
    }

    [Fact]
    public async Task SearchMenu_VegetarianOnly_ListsOnlyVegetarianDishes()
    {
        // Arrange
        MenuItemInput veggie = CreateValidInput("Veggie Burger");
        veggie.IsVegetarian = true;
        await _service.CreateMenuItemAsync(veggie);
        await _service.CreateMenuItemAsync(CreateValidInput("Beef Burger"));

        // Act
        IReadOnlyList<MenuItemCard> cards = await _service.SearchMenuAsync(
            new MenuSearchCriteria { VegetarianOnly = true });

        // Assert
        MenuItemCard card = Assert.Single(cards);
        Assert.Equal("Veggie Burger", card.Name);
    }

    [Fact]
    public async Task SearchMenu_Card_CarriesTheDietaryTags()
    {
        // Arrange
        MenuItemInput input = CreateValidInput("Hot Veggie Wrap");
        input.IsVegetarian = true;
        input.IsSpicy = true;
        await _service.CreateMenuItemAsync(input);

        // Act
        IReadOnlyList<MenuItemCard> cards = await _service.SearchMenuAsync(new MenuSearchCriteria());

        // Assert
        MenuItemCard card = Assert.Single(cards);
        Assert.True(card.IsVegetarian);
        Assert.True(card.IsSpicy);
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
