using System.Globalization;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Pages.Menu;

/// <summary>
/// The customer menu: every dish with its photo, description, current price, running offer and
/// availability, with search and category/price filters. Public: anyone can open it. Owner: YJ (YJ-2).
/// </summary>
/// <remarks>
/// /Menu shows everything, /Menu?q=burger&amp;categoryId=2&amp;maxPrice=30 filters it. The filter form uses GET,
/// so a filtered menu has its own address that can be bookmarked or shared. All pricing and availability
/// decisions are made in <see cref="IMenuService"/>; this page only displays what the service returns.
/// A "Popular this month" strip (YJ-4) appears above the full menu when no filter is in use; for a signed-in
/// customer it leaves out dishes containing a tag they avoid.
/// </remarks>
public class IndexModel : PageModel
{
    private const string OfferEndFormat = "d MMM yyyy, h:mm tt";
    private const int PopularCount = 4;

    private readonly IMenuService _menuService;
    private readonly IRestaurantService _restaurantService;
    private readonly IProfileService _profileService;
    private readonly IRecommendationService _recommendationService;
    private readonly IDiscountService _discountService;

    /// <summary>Receives the services from dependency injection.</summary>
    /// <param name="menuService">Searches the menu.</param>
    /// <param name="restaurantService">Gives the active categories for the filter drop-down.</param>
    /// <param name="profileService">Gives the shared dietary tags (YY's table) for the "hide dishes that contain" boxes.</param>
    /// <param name="recommendationService">Gives the popular dishes strip.</param>
    /// <param name="discountService">Gives the promotion banner (a sentence for each discount running now).</param>
    public IndexModel(
        IMenuService menuService,
        IRestaurantService restaurantService,
        IProfileService profileService,
        IRecommendationService recommendationService,
        IDiscountService discountService)
    {
        _menuService = menuService;
        _restaurantService = restaurantService;
        _profileService = profileService;
        _recommendationService = recommendationService;
        _discountService = discountService;
    }

    /// <summary>Words to look for in dish names and descriptions (address part ?q=).</summary>
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? SearchText { get; set; }

    /// <summary>Only this category (address part ?categoryId=), or null for all.</summary>
    [BindProperty(SupportsGet = true, Name = "categoryId")]
    public int? CategoryId { get; set; }

    /// <summary>Hide dishes that contain these dietary tags (address part ?hide=2&amp;hide=3, by tag Id).</summary>
    [BindProperty(SupportsGet = true, Name = "hide")]
    public List<int> HideTagIds { get; set; } = new List<int>();

    /// <summary>Lowest price to show (address part ?minPrice=).</summary>
    [BindProperty(SupportsGet = true, Name = "minPrice")]
    public decimal? MinPrice { get; set; }

    /// <summary>Highest price to show (address part ?maxPrice=).</summary>
    [BindProperty(SupportsGet = true, Name = "maxPrice")]
    public decimal? MaxPrice { get; set; }

    /// <summary>The dishes to show, in category then name order.</summary>
    public IReadOnlyList<MenuItemCard> Dishes { get; private set; } = new List<MenuItemCard>();

    /// <summary>One sentence for each promotion running right now (the banner above the dishes).</summary>
    public IReadOnlyList<string> Promotions { get; private set; } = new List<string>();

    /// <summary>The categories customers can pick in the filter.</summary>
    public IReadOnlyList<MenuCategory> Categories { get; private set; } = new List<MenuCategory>();

    /// <summary>Every dietary tag, in display order, for the "hide dishes that contain" boxes.</summary>
    public IReadOnlyList<DietaryTag> TagChoices { get; private set; } = new List<DietaryTag>();

    /// <summary>The "Popular this month" strip. Empty when a filter is in use or nothing was ordered lately.</summary>
    public DishSuggestionSection Popular { get; private set; } = new DishSuggestionSection();

    /// <summary>True when any filter is in use, so the page can offer a "show everything" link.</summary>
    public bool HasFilters
    {
        get
        {
            bool hasText = !string.IsNullOrWhiteSpace(SearchText);

            return hasText || CategoryId.HasValue || HideTagIds.Count > 0 || MinPrice.HasValue || MaxPrice.HasValue;
        }
    }

    /// <summary>Handles GET /Menu.</summary>
    public async Task OnGetAsync()
    {
        Categories = await _restaurantService.GetMenuCategoriesAsync(includeInactive: false);
        Promotions = await _discountService.GetRunningBannersAsync();
        TagChoices = await _profileService.GetDietaryTagsAsync();

        var criteria = new MenuSearchCriteria
        {
            SearchText = SearchText ?? string.Empty,
            MenuCategoryId = CategoryId,
            ExcludeTagIds = HideTagIds,
            MinPrice = MinPrice,
            MaxPrice = MaxPrice
        };

        Dishes = await _menuService.SearchMenuAsync(criteria);

        // The strip is for browsing the whole menu; with a filter on, it would only get in the way.
        if (!HasFilters)
        {
            string? customerId = null;

            if (User.Identity?.IsAuthenticated == true)
            {
                customerId = User.GetUserId();
            }

            Popular = new DishSuggestionSection
            {
                Heading = "Popular this month",
                Key = "popular",
                Items = await _recommendationService.GetPopularAsync(PopularCount, customerId)
            };
        }
    }

    /// <summary>Shows when an offer ends, in Malaysia time (the database keeps UTC).</summary>
    /// <param name="offerEndsAtUtc">The offer's end time in UTC.</param>
    public string FormatOfferEnd(DateTime offerEndsAtUtc)
    {
        DateTime local = MalaysiaTime.FromUtc(offerEndsAtUtc);

        return local.ToString(OfferEndFormat, CultureInfo.InvariantCulture);
    }
}
