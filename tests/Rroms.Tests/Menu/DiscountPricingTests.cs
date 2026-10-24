using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Services;

using Xunit;

namespace Rroms.Tests.Menu;

/// <summary>
/// ACCEPTANCE tests for menu discounts (YJ): which price applies at which moment, written BEFORE the code.
/// Owner: YJ.
/// </summary>
/// <remarks>
/// Pure tests: no database. 26 Oct 2026 is a Monday, 31 Oct 2026 is a Saturday. All times in the tests are
/// Malaysia time (UTC+8), converted with <see cref="MalaysiaTime"/>, because that is how the admin thinks.
/// </remarks>
public class DiscountPricingTests
{
    private const decimal NormalPrice = 20.00m;
    private const int StartersId = 1;
    private const int GrillsId = 2;
    private const int BeveragesId = 5;

    private static readonly DateTime MondayLunch = Myt(2026, 10, 26, 12, 30);
    private static readonly DateTime SaturdayLunch = Myt(2026, 10, 31, 12, 30);

    private static DateTime Myt(int year, int month, int day, int hour, int minute)
    {
        return MalaysiaTime.ToUtc(new DateTime(year, month, day, hour, minute, 0));
    }

    private static MenuItem Dish(int id = 10, int categoryId = GrillsId, decimal price = NormalPrice)
    {
        return new MenuItem { Id = id, Name = "Dish " + id, Price = price, MenuCategoryId = categoryId };
    }

    private static MenuDiscount Discount(decimal percent = 15m)
    {
        return new MenuDiscount { Id = 1, Name = "Test discount", Percent = percent, Scope = DiscountScope.WholeMenu, IsActive = true };
    }

    private static PriceQuote Quote(MenuItem dish, DateTime now, params MenuDiscount[] discounts)
    {
        return MenuItemPricing.Quote(dish, now, new DiscountSet(discounts));
    }

    // ============================== The maths ==============================//

    [Fact]
    public void Percent_TakesThatPercentOffTheNormalPrice()
    {
        PriceQuote quote = Quote(Dish(), MondayLunch, Discount(15m));

        Assert.Equal(17.00m, quote.Price);
        Assert.Equal(NormalPrice, quote.NormalPrice);
        Assert.True(quote.IsOffer);
    }

    [Fact]
    public void Percent_RoundsToTheNearestSen()
    {
        // 9.95 less 10% is 8.955, which rounds up to 8.96.
        PriceQuote quote = Quote(Dish(price: 9.95m), MondayLunch, Discount(10m));

        Assert.Equal(8.96m, quote.Price);
    }

    [Fact]
    public void NoDiscount_ReturnsTheNormalPriceAndNoOffer()
    {
        PriceQuote quote = Quote(Dish(), MondayLunch);

        Assert.Equal(NormalPrice, quote.Price);
        Assert.False(quote.IsOffer);
        Assert.Null(quote.Label);
    }

    [Fact]
    public void Label_ShowsTheDiscountNameAndPercent()
    {
        MenuDiscount discount = Discount(15m);
        discount.Name = "Weekday lunch";

        PriceQuote quote = Quote(Dish(), MondayLunch, discount);

        Assert.Equal("Weekday lunch · 15% off", quote.Label);
    }

    // ============================== What it covers ==============================//

    [Fact]
    public void Scope_WholeMenu_CoversEveryDish()
    {
        Assert.Equal(17.00m, Quote(Dish(categoryId: StartersId), MondayLunch, Discount()).Price);
        Assert.Equal(17.00m, Quote(Dish(categoryId: GrillsId), MondayLunch, Discount()).Price);
    }

    [Fact]
    public void Scope_Category_CoversOnlyThatCategory()
    {
        MenuDiscount discount = Discount();
        discount.Scope = DiscountScope.Category;
        discount.Categories.Add(new MenuCategory { Id = GrillsId, Name = "Grills" });

        Assert.Equal(17.00m, Quote(Dish(categoryId: GrillsId), MondayLunch, discount).Price);
        Assert.Equal(NormalPrice, Quote(Dish(categoryId: StartersId), MondayLunch, discount).Price);
    }

    [Fact]
    public void Scope_SeveralCategories_CoverEachOfThem()
    {
        MenuDiscount discount = Discount();
        discount.Scope = DiscountScope.Category;
        discount.Categories.Add(new MenuCategory { Id = StartersId, Name = "Starters" });
        discount.Categories.Add(new MenuCategory { Id = BeveragesId, Name = "Beverages" });

        Assert.Equal(17.00m, Quote(Dish(categoryId: StartersId), MondayLunch, discount).Price);
        Assert.Equal(17.00m, Quote(Dish(categoryId: BeveragesId), MondayLunch, discount).Price);
        Assert.Equal(NormalPrice, Quote(Dish(categoryId: GrillsId), MondayLunch, discount).Price);
    }

    [Fact]
    public void Scope_Dishes_CoversOnlyTheChosenDishes()
    {
        MenuItem chosen = Dish(id: 10);
        MenuItem other = Dish(id: 11);
        MenuDiscount discount = Discount();
        discount.Scope = DiscountScope.Dishes;
        discount.Dishes.Add(chosen);

        Assert.Equal(17.00m, Quote(chosen, MondayLunch, discount).Price);
        Assert.Equal(NormalPrice, Quote(other, MondayLunch, discount).Price);
    }

    // ============================== Switch and dates ==============================//

    [Fact]
    public void Inactive_IsIgnored()
    {
        MenuDiscount discount = Discount();
        discount.IsActive = false;

        Assert.Equal(NormalPrice, Quote(Dish(), MondayLunch, discount).Price);
    }

    [Fact]
    public void Dates_StartIsIncludedAndEndIsExcluded()
    {
        MenuDiscount discount = Discount();
        discount.StartUtc = Myt(2026, 10, 26, 0, 0);
        discount.EndUtc = Myt(2026, 10, 28, 0, 0);

        Assert.Equal(NormalPrice, Quote(Dish(), Myt(2026, 10, 25, 23, 59), discount).Price);
        Assert.Equal(17.00m, Quote(Dish(), Myt(2026, 10, 26, 0, 0), discount).Price);
        Assert.Equal(17.00m, Quote(Dish(), Myt(2026, 10, 27, 23, 59), discount).Price);
        Assert.Equal(NormalPrice, Quote(Dish(), Myt(2026, 10, 28, 0, 0), discount).Price);
    }

    [Fact]
    public void Dates_NoEnd_RunsUntilSwitchedOff()
    {
        MenuDiscount discount = Discount();
        discount.StartUtc = Myt(2026, 10, 26, 0, 0);

        Assert.Equal(17.00m, Quote(Dish(), Myt(2027, 6, 1, 12, 0), discount).Price);
    }

    // ============================== Days and hours ==============================//

    [Fact]
    public void Days_OnlyTheTickedDaysCount()
    {
        MenuDiscount discount = Discount();
        discount.DaysOfWeek = DiscountDays.Weekdays;

        Assert.Equal(17.00m, Quote(Dish(), MondayLunch, discount).Price);
        Assert.Equal(NormalPrice, Quote(Dish(), SaturdayLunch, discount).Price);
    }

    [Fact]
    public void Days_AreCountedInMalaysiaTimeNotUtc()
    {
        // 00:30 on Monday in Malaysia is still Sunday 16:30 in UTC. The discount is for Mondays.
        MenuDiscount discount = Discount();
        discount.DaysOfWeek = DiscountDays.Bit(DayOfWeek.Monday);

        Assert.Equal(17.00m, Quote(Dish(), Myt(2026, 10, 26, 0, 30), discount).Price);
    }

    [Fact]
    public void Hours_StartIsIncludedAndEndIsExcluded()
    {
        MenuDiscount discount = Discount();
        discount.FromMinute = 11 * 60;
        discount.ToMinute = 15 * 60;

        Assert.Equal(NormalPrice, Quote(Dish(), Myt(2026, 10, 26, 10, 59), discount).Price);
        Assert.Equal(17.00m, Quote(Dish(), Myt(2026, 10, 26, 11, 0), discount).Price);
        Assert.Equal(17.00m, Quote(Dish(), Myt(2026, 10, 26, 14, 59), discount).Price);
        Assert.Equal(NormalPrice, Quote(Dish(), Myt(2026, 10, 26, 15, 0), discount).Price);
    }

    // ============================== Cheaper wins, no stacking ==============================//

    [Fact]
    public void TwoDiscounts_TheBiggerOneWinsAndTheyNeverStack()
    {
        MenuDiscount small = Discount(10m);
        MenuDiscount big = Discount(20m);
        big.Id = 2;
        big.Name = "Big one";

        PriceQuote quote = Quote(Dish(), MondayLunch, small, big);

        Assert.Equal(16.00m, quote.Price);
        Assert.StartsWith("Big one", quote.Label);
    }

    [Fact]
    public void DishOffer_CheaperThanTheDiscount_KeepsTheDishOffer()
    {
        MenuItem dish = Dish();
        dish.PromoPrice = 15.00m;
        dish.PromoStartUtc = MondayLunch.AddDays(-1);
        dish.PromoEndUtc = MondayLunch.AddDays(1);

        PriceQuote quote = Quote(dish, MondayLunch, Discount(10m));

        Assert.Equal(15.00m, quote.Price);
        Assert.Null(quote.Label);
        Assert.Equal(dish.PromoEndUtc, quote.EndsAtUtc);
        Assert.Equal("Offer", quote.Badge);
        Assert.Contains("RM 18.00", quote.Note);
        Assert.Contains("so it wins", quote.Note);
    }

    [Fact]
    public void Discount_CheaperThanTheDishOffer_TakesOver()
    {
        MenuItem dish = Dish();
        dish.PromoPrice = 18.00m;
        dish.PromoStartUtc = MondayLunch.AddDays(-1);
        dish.PromoEndUtc = MondayLunch.AddDays(1);

        PriceQuote quote = Quote(dish, MondayLunch, Discount(30m));

        Assert.Equal(14.00m, quote.Price);
        Assert.NotNull(quote.Label);
        Assert.Equal("30% off", quote.Badge);
        Assert.Null(quote.EndsAtUtc);
    }

    [Fact]
    public void Note_NamesTheDiscountAndItsLastDay()
    {
        MenuDiscount discount = Discount();
        discount.Name = "Weekday Lunch";
        discount.EndUtc = Myt(2027, 1, 1, 0, 0);

        PriceQuote quote = Quote(Dish(), MondayLunch, discount);

        Assert.Equal("Weekday Lunch until 31 Dec 2026", quote.Note);
    }

    [Fact]
    public void Quote_NeverCostsMoreThanTheNormalPrice()
    {
        PriceQuote quote = Quote(Dish(), MondayLunch, Discount(1m), Discount(5m));

        Assert.True(quote.Price <= NormalPrice);
    }
}
