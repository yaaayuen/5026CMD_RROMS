using Microsoft.Extensions.Options;

using Rroms.Web.Data;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;

namespace Rroms.Web.Services;

/// <summary>
/// Implements <see cref="IRecommendationService"/> (YJ-4).
/// </summary>
/// <remarks>
/// STEP 1 of test-first development: only the public reason sentences and the constructor exist, and every
/// method throws. The acceptance tests are written against this shape and must FAIL (red). The real code comes next.
/// </remarks>
public class RecommendationService : IRecommendationService
{
    private readonly ApplicationDbContext _context;
    private readonly IMenuService _menuService;
    private readonly IProfileService _profileService;
    private readonly RecommendationOptions _options;

    /// <summary>Receives the database, the menu, YY's profile service (for the avoided tags) and the settings.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="menuService">Provides dishes with their current price, offer, tags and availability.</param>
    /// <param name="profileService">Gives the dietary tags a customer avoids.</param>
    /// <param name="options">The recommendation settings from appsettings.json (or their defaults).</param>
    public RecommendationService(
        ApplicationDbContext context,
        IMenuService menuService,
        IProfileService profileService,
        IOptions<RecommendationOptions> options)
    {
        _context = context;
        _menuService = menuService;
        _profileService = profileService;
        _options = options.Value;
    }

    /// <summary>The reason shown for a popular dish.</summary>
    /// <param name="portions">Portions ordered in the window.</param>
    /// <param name="days">The length of the window in days.</param>
    public static string PopularReason(int portions, int days)
    {
        if (portions == 1)
        {
            return $"Ordered once in the last {days} days";
        }

        return $"Ordered {portions} times in the last {days} days";
    }

    /// <summary>The reason shown for a dish often ordered together with the given dishes.</summary>
    /// <param name="orders">How many orders contained both.</param>
    public static string TogetherReason(int orders)
    {
        if (orders == 1)
        {
            return "Ordered together in 1 order";
        }

        return $"Ordered together in {orders} orders";
    }

    /// <summary>The reason shown for a dish in a category the customer often orders from.</summary>
    /// <param name="categoryName">The category's name.</param>
    public static string CategoryReason(string categoryName)
    {
        return $"Because you often order {categoryName}";
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DishRecommendation>> GetPopularAsync(int count, string? customerId = null)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DishRecommendation>> GetOftenOrderedTogetherAsync(
        IReadOnlyCollection<int> menuItemIds, int count, string? customerId = null)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DishRecommendation>> GetForCustomerAsync(string customerId, int count)
    {
        throw new NotImplementedException();
    }
}
