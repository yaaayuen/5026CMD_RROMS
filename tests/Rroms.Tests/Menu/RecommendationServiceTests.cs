using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
using Rroms.Web.Services;

namespace Rroms.Tests.Menu;

/// <summary>
/// YJ-4 acceptance tests for <see cref="RecommendationService"/>: popular dishes, dishes often ordered together,
/// and dishes suited to one customer. Owner: YJ.
/// </summary>
/// <remarks>
/// Written BEFORE the service (test-first): against the empty shell they all fail (red) and turn green as the
/// rules are implemented. Seed data in every test: five menu categories, Starters (1), Grills (2), Pasta (3),
/// Desserts (4) and Beverages (5), and YY's three dietary tags, 1 animal products, 2 dairy and 3 spicy.
/// The default settings are used unless a test says otherwise: a 30 day window, category weight 2.0, popularity weight 0.5.
/// </remarks>
public class RecommendationServiceTests : IDisposable
{
    private const int StartersId = 1;
    private const int GrillsId = 2;
    private const int PastaId = 3;
    private const int DairyTagId = 2;
    private const int SpicyTagId = 3;
    private const int WindowDays = 30;
    private const int PlentyOfDishes = 10;

    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly FakeProfileService _profiles;
    private readonly MenuService _menuService;

    /// <summary>Runs before EVERY test: a brand-new in-memory database, so tests never affect each other.</summary>
    public RecommendationServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _profiles = new FakeProfileService();
        _menuService = new MenuService(_context, new RestaurantService(_context), new FakeMenuImageStorage());
    }

    /// <summary>Runs after EVERY test: closing the connection deletes the database.</summary>
    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    /// <summary>Builds the service under test, with the default settings or the ones a test supplies.</summary>
    private RecommendationService CreateService(RecommendationOptions? settings = null)
    {
        RecommendationOptions options = settings ?? new RecommendationOptions();

        return new RecommendationService(_context, _menuService, _profiles, Options.Create(options));
    }

    private static List<string> NamesOf(IReadOnlyList<DishRecommendation> recommendations)
    {
        return recommendations.Select(recommendation => recommendation.Dish.Name).ToList();
    }

    //================================================== Popular ==================================================//

    [Fact]
    public async Task GetPopular_RanksDishesByPortionsOrdered()
    {
        // Arrange: Fries 5 portions, Burger 3, Salad 1, ordered by different customers.
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        MenuItem salad = await OrderTestData.AddDishAsync(_context, "Salad");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(salad.Id, 1), new OrderTestLine(fries.Id, 5));
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(burger.Id, 3));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetPopularAsync(PlentyOfDishes);

        // Assert
        Assert.Equal(new List<string> { "Fries", "Burger", "Salad" }, NamesOf(result));
    }

    [Fact]
    public async Task GetPopular_ReasonStatesThePortionsAndTheWindow()
    {
        // Arrange
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(fries.Id, 5));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetPopularAsync(PlentyOfDishes);

        // Assert
        DishRecommendation recommendation = Assert.Single(result);
        Assert.Equal(RecommendationService.PopularReason(5, WindowDays), recommendation.Reason);
    }

    [Fact]
    public async Task GetPopular_WithNoOrders_ReturnsNothing()
    {
        // Arrange: dishes exist but nobody has ordered.
        await OrderTestData.AddDishAsync(_context, "Fries");

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetPopularAsync(PlentyOfDishes);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPopular_IgnoresCancelledOrders()
    {
        // Arrange: Fries only appears in a cancelled order.
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, DateTime.UtcNow, OrderStatus.Cancelled, new OrderTestLine(fries.Id, 9));
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(burger.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetPopularAsync(PlentyOfDishes);

        // Assert
        Assert.Equal(new List<string> { "Burger" }, NamesOf(result));
    }

    [Fact]
    public async Task GetPopular_IgnoresOrdersOlderThanTheWindow()
    {
        // Arrange: Fries was ordered 40 days ago (outside 30 days), Burger 29 days ago (inside).
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, DateTime.UtcNow.AddDays(-40), OrderStatus.Placed, new OrderTestLine(fries.Id, 9));
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, DateTime.UtcNow.AddDays(-29), OrderStatus.Placed, new OrderTestLine(burger.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetPopularAsync(PlentyOfDishes);

        // Assert
        Assert.Equal(new List<string> { "Burger" }, NamesOf(result));
    }

    [Fact]
    public async Task GetPopular_UsesTheConfiguredWindow()
    {
        // Arrange: with a 7 day window, an order 10 days ago no longer counts.
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, DateTime.UtcNow.AddDays(-10), OrderStatus.Placed, new OrderTestLine(fries.Id, 4));
        RecommendationService service = CreateService(new RecommendationOptions { PopularityWindowDays = 7 });

        // Act
        IReadOnlyList<DishRecommendation> result = await service.GetPopularAsync(PlentyOfDishes);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPopular_NeverReturnsAnUnavailableDish()
    {
        // Arrange: the most popular dish has been switched off.
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries", isAvailable: false);
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(fries.Id, 9), new OrderTestLine(burger.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetPopularAsync(PlentyOfDishes);

        // Assert
        Assert.Equal(new List<string> { "Burger" }, NamesOf(result));
    }

    [Fact]
    public async Task GetPopular_NeverReturnsADishInAHiddenCategory()
    {
        // Arrange: the Pasta category is hidden.
        MenuItem carbonara = await OrderTestData.AddDishAsync(_context, "Carbonara", categoryId: PastaId);
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        MenuCategory pasta = (await _context.MenuCategories.FindAsync(PastaId))!;
        pasta.IsActive = false;
        await _context.SaveChangesAsync();
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(carbonara.Id, 9), new OrderTestLine(burger.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetPopularAsync(PlentyOfDishes);

        // Assert
        Assert.Equal(new List<string> { "Burger" }, NamesOf(result));
    }

    [Fact]
    public async Task GetPopular_ReturnsNoMoreThanTheCount()
    {
        // Arrange
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        MenuItem salad = await OrderTestData.AddDishAsync(_context, "Salad");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id,
            new OrderTestLine(fries.Id, 3), new OrderTestLine(burger.Id, 2), new OrderTestLine(salad.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetPopularAsync(2);

        // Assert: the two most popular.
        Assert.Equal(new List<string> { "Fries", "Burger" }, NamesOf(result));
    }

    [Fact]
    public async Task GetPopular_WhenPortionsTie_TheDishInMoreOrdersWins()
    {
        // Arrange: both dishes have 4 portions, but Burger is in two orders and Fries in one.
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(fries.Id, 4));
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(burger.Id, 2));
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(burger.Id, 2));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetPopularAsync(PlentyOfDishes);

        // Assert
        Assert.Equal(new List<string> { "Burger", "Fries" }, NamesOf(result));
    }

    [Fact]
    public async Task GetPopular_WhenEverythingTies_TheEarlierNameWins()
    {
        // Arrange: identical portions and orders.
        MenuItem zucchini = await OrderTestData.AddDishAsync(_context, "Zucchini Fries");
        MenuItem apple = await OrderTestData.AddDishAsync(_context, "Apple Pie");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(zucchini.Id, 2), new OrderTestLine(apple.Id, 2));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetPopularAsync(PlentyOfDishes);

        // Assert
        Assert.Equal(new List<string> { "Apple Pie", "Zucchini Fries" }, NamesOf(result));
    }

    [Fact]
    public async Task GetPopular_ForACustomer_HidesDishesWithATagTheyAvoid()
    {
        // Arrange: the most popular dish contains dairy, and this customer avoids dairy.
        MenuItem pizza = await OrderTestData.AddDishAsync(_context, "Cheese Pizza");
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        await RecommendationTestData.TagDishAsync(_context, pizza.Id, DairyTagId);
        ApplicationUser orderer = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        _profiles.SetAvoidedTags(customer.Id, DairyTagId);
        await RecommendationTestData.AddOrderAsync(_context, orderer.Id, new OrderTestLine(pizza.Id, 9), new OrderTestLine(burger.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetPopularAsync(PlentyOfDishes, customer.Id);

        // Assert
        Assert.Equal(new List<string> { "Burger" }, NamesOf(result));
    }

    [Fact]
    public async Task GetPopular_ForAVisitor_ShowsTaggedDishes()
    {
        // Arrange: same data, but no customer is given, so nothing is hidden.
        MenuItem pizza = await OrderTestData.AddDishAsync(_context, "Cheese Pizza");
        await RecommendationTestData.TagDishAsync(_context, pizza.Id, DairyTagId);
        ApplicationUser orderer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, orderer.Id, new OrderTestLine(pizza.Id, 9));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetPopularAsync(PlentyOfDishes);

        // Assert
        Assert.Equal(new List<string> { "Cheese Pizza" }, NamesOf(result));
    }

    [Fact]
    public async Task GetPopular_CardCarriesTheTagsAndPrice()
    {
        // Arrange
        MenuItem pizza = await OrderTestData.AddDishAsync(_context, "Cheese Pizza", price: 25.00m);
        await RecommendationTestData.TagDishAsync(_context, pizza.Id, DairyTagId);
        ApplicationUser orderer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, orderer.Id, new OrderTestLine(pizza.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetPopularAsync(PlentyOfDishes);

        // Assert
        DishRecommendation recommendation = Assert.Single(result);
        Assert.Equal(25.00m, recommendation.Dish.CurrentPrice);
        Assert.Equal(new List<string> { "Dairy" }, recommendation.Dish.DietaryTags.ToList());
    }

    //================================================== Often ordered together ==================================================//

    [Fact]
    public async Task GetOftenOrderedTogether_RanksCompanionsByHowManyOrdersHoldBoth()
    {
        // Arrange: Burger is ordered with Fries twice, with Salad once, and once alone.
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        MenuItem salad = await OrderTestData.AddDishAsync(_context, "Salad");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(burger.Id, 1), new OrderTestLine(fries.Id, 1));
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(burger.Id, 1), new OrderTestLine(fries.Id, 1), new OrderTestLine(salad.Id, 1));
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(burger.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService()
            .GetOftenOrderedTogetherAsync(new[] { burger.Id }, PlentyOfDishes);

        // Assert
        Assert.Equal(new List<string> { "Fries", "Salad" }, NamesOf(result));
    }

    [Fact]
    public async Task GetOftenOrderedTogether_ReasonStatesTheNumberOfOrders()
    {
        // Arrange
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(burger.Id, 1), new OrderTestLine(fries.Id, 1));
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(burger.Id, 1), new OrderTestLine(fries.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService()
            .GetOftenOrderedTogetherAsync(new[] { burger.Id }, PlentyOfDishes);

        // Assert
        DishRecommendation recommendation = Assert.Single(result);
        Assert.Equal(RecommendationService.TogetherReason(2), recommendation.Reason);
    }

    [Fact]
    public async Task GetOftenOrderedTogether_NeverRecommendsTheGivenDishesBack()
    {
        // Arrange: Burger and Fries are both given.
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        MenuItem salad = await OrderTestData.AddDishAsync(_context, "Salad");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id,
            new OrderTestLine(burger.Id, 1), new OrderTestLine(fries.Id, 1), new OrderTestLine(salad.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService()
            .GetOftenOrderedTogetherAsync(new[] { burger.Id, fries.Id }, PlentyOfDishes);

        // Assert
        Assert.Equal(new List<string> { "Salad" }, NamesOf(result));
    }

    [Fact]
    public async Task GetOftenOrderedTogether_CountsAnOrderOncePerDish()
    {
        // Arrange: ONE order holds both given dishes and Salad, so Salad is together with them in 1 order, not 2.
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        MenuItem salad = await OrderTestData.AddDishAsync(_context, "Salad");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id,
            new OrderTestLine(burger.Id, 1), new OrderTestLine(fries.Id, 1), new OrderTestLine(salad.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService()
            .GetOftenOrderedTogetherAsync(new[] { burger.Id, fries.Id }, PlentyOfDishes);

        // Assert
        DishRecommendation recommendation = Assert.Single(result);
        Assert.Equal(RecommendationService.TogetherReason(1), recommendation.Reason);
    }

    [Fact]
    public async Task GetOftenOrderedTogether_IgnoresCancelledOrders()
    {
        // Arrange: the only order holding both is cancelled.
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, DateTime.UtcNow, OrderStatus.Cancelled,
            new OrderTestLine(burger.Id, 1), new OrderTestLine(fries.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService()
            .GetOftenOrderedTogetherAsync(new[] { burger.Id }, PlentyOfDishes);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOftenOrderedTogether_WithNoGivenDishes_ReturnsNothing()
    {
        // Arrange
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(burger.Id, 1), new OrderTestLine(fries.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService()
            .GetOftenOrderedTogetherAsync(new List<int>(), PlentyOfDishes);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOftenOrderedTogether_NeverReturnsAnUnavailableDish()
    {
        // Arrange
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries", isAvailable: false);
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(burger.Id, 1), new OrderTestLine(fries.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService()
            .GetOftenOrderedTogetherAsync(new[] { burger.Id }, PlentyOfDishes);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetOftenOrderedTogether_ForACustomer_HidesDishesWithATagTheyAvoid()
    {
        // Arrange: Spicy Wings are the best companion, but the customer avoids spicy dishes.
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        MenuItem wings = await OrderTestData.AddDishAsync(_context, "Spicy Wings");
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        await RecommendationTestData.TagDishAsync(_context, wings.Id, SpicyTagId);
        ApplicationUser orderer = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        _profiles.SetAvoidedTags(customer.Id, SpicyTagId);
        await RecommendationTestData.AddOrderAsync(_context, orderer.Id, new OrderTestLine(burger.Id, 1), new OrderTestLine(wings.Id, 1), new OrderTestLine(fries.Id, 1));
        await RecommendationTestData.AddOrderAsync(_context, orderer.Id, new OrderTestLine(burger.Id, 1), new OrderTestLine(wings.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService()
            .GetOftenOrderedTogetherAsync(new[] { burger.Id }, PlentyOfDishes, customer.Id);

        // Assert
        Assert.Equal(new List<string> { "Fries" }, NamesOf(result));
    }

    [Fact]
    public async Task GetOftenOrderedTogether_ReturnsNoMoreThanTheCount()
    {
        // Arrange
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        MenuItem salad = await OrderTestData.AddDishAsync(_context, "Salad");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id,
            new OrderTestLine(burger.Id, 1), new OrderTestLine(fries.Id, 1), new OrderTestLine(salad.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService()
            .GetOftenOrderedTogetherAsync(new[] { burger.Id }, 1);

        // Assert
        Assert.Single(result);
    }

    //================================================== For one customer ==================================================//

    [Fact]
    public async Task GetForCustomer_PutsTheCategoryTheyOrderMostFirst_AndPopularityBreaksTheRest()
    {
        // Arrange: the customer only ever orders Grills (Steak, 3 portions). Someone else orders a lot of Pasta.
        // Scores with the default weights (category 2.0, popularity 0.5), most popular dish = Carbonara with 6:
        //   Steak 2.0 x 1 + 0.5 x (3/6) = 2.25     Ribs 2.0 x 1 + 0 = 2.0     Carbonara 0 + 0.5 x 1 = 0.5
        MenuItem steak = await OrderTestData.AddDishAsync(_context, "Steak", categoryId: GrillsId);
        MenuItem ribs = await OrderTestData.AddDishAsync(_context, "Ribs", categoryId: GrillsId);
        MenuItem carbonara = await OrderTestData.AddDishAsync(_context, "Carbonara", categoryId: PastaId);
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser other = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(steak.Id, 3));
        await RecommendationTestData.AddOrderAsync(_context, other.Id, new OrderTestLine(carbonara.Id, 6));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetForCustomerAsync(customer.Id, PlentyOfDishes);

        // Assert
        Assert.Equal(new List<string> { "Steak", "Ribs", "Carbonara" }, NamesOf(result));
    }

    [Fact]
    public async Task GetForCustomer_ReasonNamesTheCategoryForFamiliarDishesAndPopularityForTheRest()
    {
        // Arrange: same data as above.
        MenuItem steak = await OrderTestData.AddDishAsync(_context, "Steak", categoryId: GrillsId);
        MenuItem ribs = await OrderTestData.AddDishAsync(_context, "Ribs", categoryId: GrillsId);
        MenuItem carbonara = await OrderTestData.AddDishAsync(_context, "Carbonara", categoryId: PastaId);
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser other = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(steak.Id, 3));
        await RecommendationTestData.AddOrderAsync(_context, other.Id, new OrderTestLine(carbonara.Id, 6));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetForCustomerAsync(customer.Id, PlentyOfDishes);

        // Assert
        Assert.Equal(RecommendationService.CategoryReason("Grills"), result[1].Reason);
        Assert.Equal(RecommendationService.PopularReason(6, WindowDays), result[2].Reason);
    }

    [Fact]
    public async Task GetForCustomer_LeavesOutDishesWithNoReasonToBeThere()
    {
        // Arrange: Tiramisu is in a category the customer never orders from and nobody has ordered it.
        MenuItem steak = await OrderTestData.AddDishAsync(_context, "Steak", categoryId: GrillsId);
        await OrderTestData.AddDishAsync(_context, "Tiramisu", categoryId: StartersId);
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(steak.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetForCustomerAsync(customer.Id, PlentyOfDishes);

        // Assert
        Assert.Equal(new List<string> { "Steak" }, NamesOf(result));
    }

    [Fact]
    public async Task GetForCustomer_WithNoHistory_FallsBackToThePopularDishes()
    {
        // Arrange: this customer never ordered, but other customers did.
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        MenuItem burger = await OrderTestData.AddDishAsync(_context, "Burger");
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser other = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, other.Id, new OrderTestLine(fries.Id, 5), new OrderTestLine(burger.Id, 2));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetForCustomerAsync(customer.Id, PlentyOfDishes);

        // Assert: the popular list, with the popular reason.
        Assert.Equal(new List<string> { "Fries", "Burger" }, NamesOf(result));
        Assert.Equal(RecommendationService.PopularReason(5, WindowDays), result[0].Reason);
    }

    [Fact]
    public async Task GetForCustomer_ForAnUnknownCustomer_FallsBackToThePopularDishes()
    {
        // Arrange: the id belongs to nobody.
        MenuItem fries = await OrderTestData.AddDishAsync(_context, "Fries");
        ApplicationUser other = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, other.Id, new OrderTestLine(fries.Id, 5));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetForCustomerAsync("nobody", PlentyOfDishes);

        // Assert
        Assert.Equal(new List<string> { "Fries" }, NamesOf(result));
    }

    [Fact]
    public async Task GetForCustomer_IgnoresTheCustomersCancelledOrdersWhenLearningTheirHabits()
    {
        // Arrange: the customer's only order was cancelled, so they have no real history.
        MenuItem steak = await OrderTestData.AddDishAsync(_context, "Steak", categoryId: GrillsId);
        MenuItem carbonara = await OrderTestData.AddDishAsync(_context, "Carbonara", categoryId: PastaId);
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser other = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, DateTime.UtcNow, OrderStatus.Cancelled, new OrderTestLine(steak.Id, 5));
        await RecommendationTestData.AddOrderAsync(_context, other.Id, new OrderTestLine(carbonara.Id, 2));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetForCustomerAsync(customer.Id, PlentyOfDishes);

        // Assert: the popular list only, nothing based on the cancelled steak order.
        Assert.Equal(new List<string> { "Carbonara" }, NamesOf(result));
    }

    [Fact]
    public async Task GetForCustomer_NeverUsesAnotherCustomersHabits()
    {
        // Arrange: customer A orders Grills, customer B orders Pasta. A must be recommended Grills first.
        MenuItem steak = await OrderTestData.AddDishAsync(_context, "Steak", categoryId: GrillsId);
        MenuItem ribs = await OrderTestData.AddDishAsync(_context, "Ribs", categoryId: GrillsId);
        MenuItem carbonara = await OrderTestData.AddDishAsync(_context, "Carbonara", categoryId: PastaId);
        MenuItem lasagne = await OrderTestData.AddDishAsync(_context, "Lasagne", categoryId: PastaId);
        ApplicationUser customerA = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser customerB = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customerA.Id, new OrderTestLine(steak.Id, 1));
        await RecommendationTestData.AddOrderAsync(_context, customerB.Id, new OrderTestLine(carbonara.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> forA = await CreateService().GetForCustomerAsync(customerA.Id, 2);
        IReadOnlyList<DishRecommendation> forB = await CreateService().GetForCustomerAsync(customerB.Id, 2);

        // Assert: each customer's two best dishes are from their own category.
        Assert.Equal(new List<string> { "Ribs", "Steak" }, NamesOf(forA).OrderBy(name => name).ToList());
        Assert.Equal(new List<string> { "Carbonara", "Lasagne" }, NamesOf(forB).OrderBy(name => name).ToList());
    }

    [Fact]
    public async Task GetForCustomer_NeverRecommendsADishWithATagTheyAvoid()
    {
        // Arrange: Ribs is in the customer's favourite category but contains dairy, which they avoid.
        MenuItem steak = await OrderTestData.AddDishAsync(_context, "Steak", categoryId: GrillsId);
        MenuItem ribs = await OrderTestData.AddDishAsync(_context, "Ribs", categoryId: GrillsId);
        await RecommendationTestData.TagDishAsync(_context, ribs.Id, DairyTagId);
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        _profiles.SetAvoidedTags(customer.Id, DairyTagId);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(steak.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetForCustomerAsync(customer.Id, PlentyOfDishes);

        // Assert
        Assert.Equal(new List<string> { "Steak" }, NamesOf(result));
    }

    [Fact]
    public async Task GetForCustomer_NeverRecommendsAnUnavailableDish()
    {
        // Arrange
        MenuItem steak = await OrderTestData.AddDishAsync(_context, "Steak", categoryId: GrillsId);
        await OrderTestData.AddDishAsync(_context, "Ribs", categoryId: GrillsId, isAvailable: false);
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(steak.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetForCustomerAsync(customer.Id, PlentyOfDishes);

        // Assert
        Assert.Equal(new List<string> { "Steak" }, NamesOf(result));
    }

    [Fact]
    public async Task GetForCustomer_ReturnsNoMoreThanTheCount()
    {
        // Arrange
        MenuItem steak = await OrderTestData.AddDishAsync(_context, "Steak", categoryId: GrillsId);
        await OrderTestData.AddDishAsync(_context, "Ribs", categoryId: GrillsId);
        await OrderTestData.AddDishAsync(_context, "Chops", categoryId: GrillsId);
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(steak.Id, 1));

        // Act
        IReadOnlyList<DishRecommendation> result = await CreateService().GetForCustomerAsync(customer.Id, 2);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetForCustomer_UsesTheConfiguredWeights()
    {
        // Arrange: with the category weight at 0 and popularity at 1, only popularity counts, so the popular
        // Pasta dish beats the customer's own category.
        MenuItem steak = await OrderTestData.AddDishAsync(_context, "Steak", categoryId: GrillsId);
        MenuItem carbonara = await OrderTestData.AddDishAsync(_context, "Carbonara", categoryId: PastaId);
        ApplicationUser customer = await OrderTestData.AddCustomerAsync(_context);
        ApplicationUser other = await OrderTestData.AddCustomerAsync(_context);
        await RecommendationTestData.AddOrderAsync(_context, customer.Id, new OrderTestLine(steak.Id, 1));
        await RecommendationTestData.AddOrderAsync(_context, other.Id, new OrderTestLine(carbonara.Id, 9));
        RecommendationService service = CreateService(new RecommendationOptions { CategoryAffinityWeight = 0.0, PopularityWeight = 1.0 });

        // Act
        IReadOnlyList<DishRecommendation> result = await service.GetForCustomerAsync(customer.Id, PlentyOfDishes);

        // Assert
        Assert.Equal("Carbonara", result[0].Dish.Name);
    }

    //================================================== Reason wording ==================================================//

    [Fact]
    public void PopularReason_SaysOnceForOnePortion()
    {
        Assert.Equal("Ordered once in the last 30 days", RecommendationService.PopularReason(1, 30));
    }

    [Fact]
    public void PopularReason_SaysTheNumberOfTimes()
    {
        Assert.Equal("Ordered 12 times in the last 30 days", RecommendationService.PopularReason(12, 30));
    }

    [Fact]
    public void TogetherAndCategoryReasons_AreReadable()
    {
        Assert.Equal("Ordered together in 1 order", RecommendationService.TogetherReason(1));
        Assert.Equal("Ordered together in 3 orders", RecommendationService.TogetherReason(3));
        Assert.Equal("Because you often order Grills", RecommendationService.CategoryReason("Grills"));
    }
}
