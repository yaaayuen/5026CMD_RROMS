using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Menu;

/// <summary>
/// Menu items list: every dish with its category, the price customers pay now and its availability, plus quick
/// "turn ordering on or off" and delete buttons. Can be searched and filtered. Owner: YJ (YJ-1).
/// </summary>
/// <remarks>
/// /Admin/Menu lists every dish. The address can carry filters: ?categoryId=2 (one category),
/// ?q=burger (name or code contains the text) and ?status=available or ?status=unavailable.
/// Adding and editing happen on the Edit page. Pages never touch the database: they call the services.
/// Only administrators can open this page, because the whole Admin area is protected in Program.cs.
/// </remarks>
public class IndexModel : PageModel
{
    /// <summary>Value of the status filter that shows only dishes customers can order.</summary>
    public const string StatusAvailable = "available";

    /// <summary>Value of the status filter that shows only dishes switched off.</summary>
    public const string StatusUnavailable = "unavailable";

    private readonly IMenuService _menuService;
    private readonly IRestaurantService _restaurantService;
    private readonly IMenuImageStorage _imageStorage;

    private IReadOnlyDictionary<int, PriceQuote> _quotes = new Dictionary<int, PriceQuote>();

    /// <summary>Receives the services from dependency injection.</summary>
    /// <param name="menuService">The menu item service.</param>
    /// <param name="restaurantService">Gives the menu categories for the filter.</param>
    /// <param name="imageStorage">Turns a stored photo name into a web address.</param>
    public IndexModel(IMenuService menuService, IRestaurantService restaurantService, IMenuImageStorage imageStorage)
    {
        _menuService = menuService;
        _restaurantService = restaurantService;
        _imageStorage = imageStorage;
    }

    /// <summary>The dishes to show (after the filters), in category then name order.</summary>
    public IReadOnlyList<MenuItem> Items { get; private set; } = new List<MenuItem>();

    /// <summary>How many dishes there are before the search and status filters (the category filter still applies).</summary>
    public int TotalCount { get; private set; }

    /// <summary>Every category, including hidden ones, for the filter drop-down.</summary>
    public IReadOnlyList<MenuCategory> Categories { get; private set; } = new List<MenuCategory>();

    /// <summary>The category to filter by, or null for every dish. Comes from the address (?categoryId=2).</summary>
    [BindProperty(SupportsGet = true)]
    public int? CategoryId { get; set; }

    /// <summary>Text the dish name or code must contain, or empty for no search. Comes from the address (?q=burger).</summary>
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    /// <summary>"available", "unavailable" or empty for both. Comes from the address (?status=available).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    /// <summary>True when the search or the status filter is hiding some dishes.</summary>
    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || NormalisedStatus != null;

    /// <summary>The status filter in a safe form: only "available" or "unavailable", otherwise null.</summary>
    private string? NormalisedStatus
    {
        get
        {
            string status = (Status ?? string.Empty).Trim().ToLowerInvariant();

            if (status == StatusAvailable || status == StatusUnavailable)
            {
                return status;
            }

            return null;
        }
    }

    /// <summary>Handles GET /Admin/Menu.</summary>
    public async Task OnGetAsync()
    {
        Categories = await _restaurantService.GetMenuCategoriesAsync(includeInactive: true);

        IReadOnlyList<MenuItem> all = await _menuService.GetMenuItemsAsync(CategoryId);
        TotalCount = all.Count;
        Items = Filter(all);
        _quotes = await _menuService.GetCurrentPricesAsync(Items);
    }

    /// <summary>Handles the availability switch.</summary>
    /// <param name="id">The dish to change.</param>
    /// <param name="makeAvailable">True to allow ordering, false to block it.</param>
    public async Task<IActionResult> OnPostToggleAsync(int id, bool makeAvailable)
    {
        ServiceResult result = await _menuService.SetAvailabilityAsync(id, makeAvailable);

        if (result.Succeeded)
        {
            TempData["StatusMessage"] = makeAvailable ? "The dish can be ordered again." : "The dish can no longer be ordered.";
        }
        else
        {
            TempData["ErrorMessage"] = string.Join(" ", result.Errors);
        }

        return RedirectBackToTheList();
    }

    /// <summary>Handles the Delete button (after the confirmation dialog).</summary>
    /// <param name="id">The dish to delete.</param>
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        ServiceResult result = await _menuService.DeleteMenuItemAsync(id);

        if (result.Succeeded)
        {
            TempData["StatusMessage"] = "Dish deleted.";
        }
        else
        {
            TempData["ErrorMessage"] = string.Join(" ", result.Errors);
        }

        return RedirectBackToTheList();
    }

    /// <summary>What the customer pays for the dish right now, and whether a promotion or an offer is behind it.</summary>
    /// <param name="item">The dish.</param>
    public PriceQuote GetQuote(MenuItem item)
    {
        if (_quotes.TryGetValue(item.Id, out PriceQuote? quote))
        {
            return quote;
        }

        return new PriceQuote { NormalPrice = item.Price, Price = item.Price };
    }

    /// <summary>The web address of the dish's photo, or null when it has none.</summary>
    /// <param name="item">The dish.</param>
    public string? GetPhotoUrl(MenuItem item)
    {
        if (string.IsNullOrEmpty(item.ImageFileName))
        {
            return null;
        }

        return _imageStorage.GetPublicUrl(item.ImageFileName);
    }

    /// <summary>The first letter of the dish name, shown on the coloured tile when the dish has no photo.</summary>
    /// <param name="item">The dish.</param>
    public string GetLetter(MenuItem item)
    {
        string name = (item.Name ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            return "?";
        }

        return name.Substring(0, 1).ToUpperInvariant();
    }

    /// <summary>Applies the search text and the status filter to the dishes.</summary>
    /// <param name="all">The dishes after the category filter.</param>
    private List<MenuItem> Filter(IReadOnlyList<MenuItem> all)
    {
        IEnumerable<MenuItem> result = all;
        string text = (Search ?? string.Empty).Trim();

        if (text.Length > 0)
        {
            result = result.Where(item =>
                (item.Name ?? string.Empty).Contains(text, StringComparison.OrdinalIgnoreCase)
                || (item.Code ?? string.Empty).Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        string? status = NormalisedStatus;

        if (status == StatusAvailable)
        {
            result = result.Where(item => item.IsAvailable);
        }
        else if (status == StatusUnavailable)
        {
            result = result.Where(item => !item.IsAvailable);
        }

        return result.ToList();
    }

    /// <summary>Goes back to the list with the same search and filters the administrator had.</summary>
    private IActionResult RedirectBackToTheList()
    {
        return RedirectToPage(new { categoryId = CategoryId, q = Search, status = NormalisedStatus });
    }
}
