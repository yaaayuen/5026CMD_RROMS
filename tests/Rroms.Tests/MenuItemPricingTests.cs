using Rroms.Web.Models;
using Rroms.Web.Services;

using Xunit;

namespace Rroms.Tests;

/// <summary>
/// Tests for MenuItemPricing: which price applies at which moment.
/// </summary>
public class MenuItemPricingTests
{
    private const decimal NormalPrice = 20.00m;
    private const decimal OfferPrice = 15.00m;

    private static readonly DateTime PromoStart = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PromoEnd = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);

    private static MenuItem CreateItemWithPromo()
    {
        MenuItem item = new MenuItem
        {
            Name = "Test Burger",
            Price = NormalPrice,
            PromoPrice = OfferPrice,
            PromoStartUtc = PromoStart,
            PromoEndUtc = PromoEnd
        };

        return item;
    }

    [Fact]
    public void GetCurrentPrice_NoPromo_ReturnsNormalPrice()
    {
        MenuItem item = new MenuItem { Name = "Plain", Price = NormalPrice };

        decimal price = MenuItemPricing.GetCurrentPrice(item, PromoStart);

        Assert.Equal(NormalPrice, price);
    }

    [Fact]
    public void GetCurrentPrice_BeforeWindow_ReturnsNormalPrice()
    {
        MenuItem item = CreateItemWithPromo();

        decimal price = MenuItemPricing.GetCurrentPrice(item, PromoStart.AddSeconds(-1));

        Assert.Equal(NormalPrice, price);
    }

    [Fact]
    public void GetCurrentPrice_ExactlyAtStart_ReturnsPromoPrice()
    {
        MenuItem item = CreateItemWithPromo();

        decimal price = MenuItemPricing.GetCurrentPrice(item, PromoStart);

        Assert.Equal(OfferPrice, price);
    }

    [Fact]
    public void GetCurrentPrice_InsideWindow_ReturnsPromoPrice()
    {
        MenuItem item = CreateItemWithPromo();

        decimal price = MenuItemPricing.GetCurrentPrice(item, PromoStart.AddDays(3));

        Assert.Equal(OfferPrice, price);
    }

    [Fact]
    public void GetCurrentPrice_ExactlyAtEnd_ReturnsNormalPrice()
    {
        MenuItem item = CreateItemWithPromo();

        decimal price = MenuItemPricing.GetCurrentPrice(item, PromoEnd);

        Assert.Equal(NormalPrice, price);
    }

    [Fact]
    public void GetCurrentPrice_AfterWindow_ReturnsNormalPrice()
    {
        MenuItem item = CreateItemWithPromo();

        decimal price = MenuItemPricing.GetCurrentPrice(item, PromoEnd.AddDays(1));

        Assert.Equal(NormalPrice, price);
    }

    [Fact]
    public void IsPromoActive_PromoPriceWithoutDates_ReturnsFalse()
    {
        MenuItem item = new MenuItem { Name = "Half set", Price = NormalPrice, PromoPrice = OfferPrice };

        bool active = MenuItemPricing.IsPromoActive(item, PromoStart);

        Assert.False(active);
    }
}