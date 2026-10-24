using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Rroms.Web.Common;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

using Xunit;

namespace Rroms.Tests.Menu;

/// <summary>
/// ACCEPTANCE tests: the customer menu shows the discounted price, using a real (in-memory SQLite) database.
/// Owner: YJ.
/// </summary>
/// <remarks>These tests use "right now" because <see cref="MenuService"/> reads the clock itself,
/// so each discount runs every day, all day, and only the percent and scope change.</remarks>
public class MenuServiceDiscountTests : IDisposable
{
    private const int GrillsId = 2;
    private const int BeveragesId = 5;

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly MenuService _service;

    public MenuServiceDiscountTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _service = new MenuService(_context, new RestaurantService(_context), new FakeMenuImageStorage());
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private async Task<MenuItem> AddDishAsync(string name, int categoryId)
    {
        MenuItem dish = new MenuItem
        {
            Name = name,
            Description = "Test dish",
            MenuCategoryId = categoryId,
            Price = 20.00m,
            IsAvailable = true
        };
        _context.MenuItems.Add(dish);
        await _context.SaveChangesAsync();

        return dish;
    }

    private async Task AddDiscountAsync(decimal percent, DiscountScope scope, int? categoryId = null)
    {
        MenuDiscount discount = new MenuDiscount
        {
            Name = "Test discount",
            Percent = percent,
            Scope = scope,
            IsActive = true
        };

        if (categoryId.HasValue)
        {
            discount.Categories.Add(await _context.MenuCategories.SingleAsync(c => c.Id == categoryId.Value));
        }

        _context.MenuDiscounts.Add(discount);
        await _context.SaveChangesAsync();
    }

    private async Task<MenuItemCard> FindCardAsync(string name)
    {
        IReadOnlyList<MenuItemCard> cards = await _service.SearchMenuAsync(new MenuSearchCriteria());

        return cards.Single(card => card.Name == name);
    }

    [Fact]
    public async Task Menu_ShowsTheDiscountedPriceAndTheOfferLabel()
    {
        await AddDishAsync("Disc burger", GrillsId);
        await AddDiscountAsync(15m, DiscountScope.WholeMenu);

        MenuItemCard card = await FindCardAsync("Disc burger");

        Assert.Equal(17.00m, card.CurrentPrice);
        Assert.Equal(20.00m, card.NormalPrice);
        Assert.True(card.IsOfferActive);
        Assert.Equal("Test discount · 15% off", card.OfferLabel);
        Assert.Equal("15% off", card.OfferBadge);
        Assert.Equal("Test discount", card.OfferNote);
    }

    [Fact]
    public async Task Menu_CategoryDiscount_LeavesOtherCategoriesAlone()
    {
        await AddDishAsync("Disc grill", GrillsId);
        await AddDishAsync("Disc drink", BeveragesId);
        await AddDiscountAsync(50m, DiscountScope.Category, GrillsId);

        Assert.Equal(10.00m, (await FindCardAsync("Disc grill")).CurrentPrice);
        Assert.Equal(20.00m, (await FindCardAsync("Disc drink")).CurrentPrice);
        Assert.False((await FindCardAsync("Disc drink")).IsOfferActive);
    }

    [Fact]
    public async Task Menu_PriceFilterUsesTheDiscountedPrice()
    {
        await AddDishAsync("Disc filter", GrillsId);
        await AddDiscountAsync(50m, DiscountScope.WholeMenu);

        IReadOnlyList<MenuItemCard> cheap = await _service.SearchMenuAsync(new MenuSearchCriteria { MaxPrice = 12m });

        Assert.Contains(cheap, card => card.Name == "Disc filter");
    }

    [Fact]
    public async Task Menu_SwitchedOffDiscount_ShowsTheNormalPrice()
    {
        await AddDishAsync("Disc off", GrillsId);
        await AddDiscountAsync(15m, DiscountScope.WholeMenu);
        (await _context.MenuDiscounts.SingleAsync()).IsActive = false;
        await _context.SaveChangesAsync();

        MenuItemCard card = await FindCardAsync("Disc off");

        Assert.Equal(20.00m, card.CurrentPrice);
        Assert.False(card.IsOfferActive);
        Assert.Null(card.OfferLabel);
    }

    /// <summary>A stand-in for the real photo storage: these tests use no photos.</summary>
    private class FakeMenuImageStorage : IMenuImageStorage
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
