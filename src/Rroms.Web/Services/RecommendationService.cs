using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;

namespace Rroms.Web.Services;

/// <summary>
/// Implements <see cref="IRecommendationService"/> (YJ-4).
/// </summary>
/// <remarks>
/// <para>
/// Rule based and explainable. Popularity is a count of portions ordered in the last few days. "Together" counts
/// how many orders hold two dishes. "For you" adds up two numbers: how much of the customer's own ordering is in
/// the dish's category, and how popular the dish is. The numbers that shape the result are in
/// <see cref="RecommendationOptions"/>.
/// </para>
/// <para>
/// Safety rules, applied to every list: the dishes come from <see cref="IMenuService.SearchMenuAsync"/>, which
/// already hides dishes in hidden categories and, given the customer's avoided tags, dishes containing them;
/// then dishes that cannot be ordered are dropped; cancelled orders never count.
/// </para>
/// <para>
/// The order lines are loaded and counted in C# (the same load-then-calculate approach as the monitoring
/// reports), because the menu and the order history of a single restaurant are small.
/// </para>
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
    public async Task<IReadOnlyList<DishRecommendation>> GetPopularAsync(int count, string? customerId = null)
    {
        if (count <= 0)
        {
            return new List<DishRecommendation>();
        }

        Dictionary<int, DishPopularity> popularity = await LoadPopularityAsync();
        List<MenuItemCard> candidates = await LoadCandidatesAsync(customerId);

        List<DishRecommendation> result = candidates
            .Where(dish => popularity.ContainsKey(dish.Id))
            .OrderByDescending(dish => popularity[dish.Id].Portions)
            .ThenByDescending(dish => popularity[dish.Id].Orders)
            .ThenBy(dish => dish.Name, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .Select(dish => new DishRecommendation
            {
                Dish = dish,
                Reason = PopularReason(popularity[dish.Id].Portions, _options.PopularityWindowDays)
            })
            .ToList();

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DishRecommendation>> GetOftenOrderedTogetherAsync(
        IReadOnlyCollection<int> menuItemIds, int count, string? customerId = null)
    {
        if (count <= 0 || menuItemIds.Count == 0)
        {
            return new List<DishRecommendation>();
        }

        List<int> given = menuItemIds.Distinct().ToList();

        // Step 1: the orders (not cancelled) that hold at least one of the given dishes.
        List<int> orderIds = await _context.OrderItems
            .Where(line => given.Contains(line.MenuItemId) && line.Order!.Status != OrderStatus.Cancelled)
            .Select(line => line.OrderId)
            .Distinct()
            .ToListAsync();

        // Step 2: every OTHER dish in those orders, counted once per order.
        var companionLines = await _context.OrderItems
            .Where(line => orderIds.Contains(line.OrderId) && !given.Contains(line.MenuItemId))
            .Select(line => new { line.MenuItemId, line.OrderId })
            .ToListAsync();

        Dictionary<int, int> ordersTogether = companionLines
            .GroupBy(line => line.MenuItemId)
            .ToDictionary(group => group.Key, group => group.Select(line => line.OrderId).Distinct().Count());

        List<MenuItemCard> candidates = await LoadCandidatesAsync(customerId);

        List<DishRecommendation> result = candidates
            .Where(dish => ordersTogether.ContainsKey(dish.Id))
            .OrderByDescending(dish => ordersTogether[dish.Id])
            .ThenBy(dish => dish.Name, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .Select(dish => new DishRecommendation
            {
                Dish = dish,
                Reason = TogetherReason(ordersTogether[dish.Id])
            })
            .ToList();

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DishRecommendation>> GetForCustomerAsync(string customerId, int count)
    {
        if (count <= 0)
        {
            return new List<DishRecommendation>();
        }

        // The customer's own habit: portions ordered per category, from orders that were not cancelled.
        var habitLines = await _context.OrderItems
            .Where(line => line.Order!.CustomerId == customerId && line.Order.Status != OrderStatus.Cancelled)
            .Select(line => new { line.MenuItem!.MenuCategoryId, line.Quantity })
            .ToListAsync();

        Dictionary<int, int> portionsPerCategory = habitLines
            .GroupBy(line => line.MenuCategoryId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));

        int totalPortions = portionsPerCategory.Values.Sum();

        if (totalPortions == 0)
        {
            // No usable history yet: everyone gets the popular dishes.
            return await GetPopularAsync(count, customerId);
        }

        Dictionary<int, DishPopularity> popularity = await LoadPopularityAsync();
        int mostPortions = popularity.Count == 0 ? 0 : popularity.Values.Max(stats => stats.Portions);

        List<MenuItemCard> candidates = await LoadCandidatesAsync(customerId);

        // The cards carry the category name but not its Id, so the Ids are read separately.
        List<int> candidateIds = candidates.Select(dish => dish.Id).ToList();
        Dictionary<int, int> categoryOfDish = await _context.MenuItems
            .Where(item => candidateIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.MenuCategoryId);

        List<ScoredDish> scored = new List<ScoredDish>();

        foreach (MenuItemCard dish in candidates)
        {
            double categoryShare = 0.0;

            if (categoryOfDish.TryGetValue(dish.Id, out int categoryId)
                && portionsPerCategory.TryGetValue(categoryId, out int categoryPortions))
            {
                categoryShare = (double)categoryPortions / totalPortions;
            }

            double popularityShare = 0.0;

            if (mostPortions > 0 && popularity.TryGetValue(dish.Id, out DishPopularity? stats))
            {
                popularityShare = (double)stats.Portions / mostPortions;
            }

            double score = (_options.CategoryAffinityWeight * categoryShare) + (_options.PopularityWeight * popularityShare);

            if (score <= 0.0)
            {
                // Nothing speaks for this dish: not their habit and not popular.
                continue;
            }

            string reason;

            if (categoryShare > 0.0)
            {
                reason = CategoryReason(dish.CategoryName);
            }
            else
            {
                reason = PopularReason(popularity[dish.Id].Portions, _options.PopularityWindowDays);
            }

            scored.Add(new ScoredDish(dish, score, reason));
        }

        List<DishRecommendation> result = scored
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Dish.Name, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .Select(item => new DishRecommendation { Dish = item.Dish, Reason = item.Reason })
            .ToList();

        return result;
    }

    /// <summary>
    /// Portions and orders per dish in the popularity window. Cancelled orders and older orders are not counted.
    /// </summary>
    private async Task<Dictionary<int, DishPopularity>> LoadPopularityAsync()
    {
        DateTime since = DateTime.UtcNow.AddDays(-_options.PopularityWindowDays);

        var lines = await _context.OrderItems
            .Where(line => line.Order!.Status != OrderStatus.Cancelled && line.Order.PlacedAtUtc >= since)
            .Select(line => new { line.MenuItemId, line.OrderId, line.Quantity })
            .ToListAsync();

        Dictionary<int, DishPopularity> popularity = lines
            .GroupBy(line => line.MenuItemId)
            .ToDictionary(
                group => group.Key,
                group => new DishPopularity(
                    group.Sum(line => line.Quantity),
                    group.Select(line => line.OrderId).Distinct().Count()));

        return popularity;
    }

    /// <summary>
    /// The dishes that may be recommended: shown on the menu, orderable right now, and free of every dietary tag
    /// the given customer avoids. With no customer (a visitor) nothing is hidden for dietary reasons.
    /// </summary>
    private async Task<List<MenuItemCard>> LoadCandidatesAsync(string? customerId)
    {
        List<int> avoidedTagIds = new List<int>();

        if (!string.IsNullOrEmpty(customerId))
        {
            IReadOnlyList<int> avoided = await _profileService.GetAvoidedTagIdsAsync(customerId);
            avoidedTagIds = avoided.ToList();
        }

        IReadOnlyList<MenuItemCard> cards = await _menuService.SearchMenuAsync(
            new MenuSearchCriteria { ExcludeTagIds = avoidedTagIds });

        return cards.Where(dish => dish.CanOrder).ToList();
    }

    /// <summary>How often a dish was ordered in the window.</summary>
    /// <param name="Portions">Total portions.</param>
    /// <param name="Orders">In how many different orders.</param>
    private sealed record DishPopularity(int Portions, int Orders);

    /// <summary>A candidate dish with its score and the sentence explaining it.</summary>
    /// <param name="Dish">The dish.</param>
    /// <param name="Score">Higher is a better fit.</param>
    /// <param name="Reason">Why it is suggested.</param>
    private sealed record ScoredDish(MenuItemCard Dish, double Score, string Reason);
}