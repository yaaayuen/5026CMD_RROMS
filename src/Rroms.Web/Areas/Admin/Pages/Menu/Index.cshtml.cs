using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Menu;

/// <summary>
/// Menu items list: every dish with its category, current price and availability, plus quick
/// "turn ordering on or off" and delete buttons. Owner: YJ (YJ-1).
/// </summary>
/// <remarks>
/// /Admin/Menu lists every dish, /Admin/Menu?categoryId=2 shows only one category.
/// Adding and editing happen on the Edit page. Pages never touch the database: they call the services.
/// Only administrators can open this page, because the whole Admin area is protected in Program.cs.
/// </remarks>
public class IndexModel : PageModel
{
    private readonly IMenuService _menuService;
    private readonly IRestaurantService _restaurantService;
    private readonly IMenuImageStorage _imageStorage;

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

    /// <summary>The dishes to show, in category then name order.</summary>
    public IReadOnlyList<MenuItem> Items { get; private set; } = new List<MenuItem>();

    /// <summary>Every category, including hidden ones, for the filter drop-down.</summary>
    public IReadOnlyList<MenuCategory> Categories { get; private set; } = new List<MenuCategory>();

    /// <summary>The category to filter by, or null for every dish. Comes from the address (?categoryId=2).</summary>
    [BindProperty(SupportsGet = true)]
    public int? CategoryId { get; set; }

    /// <summary>Handles GET /Admin/Menu.</summary>
    public async Task OnGetAsync()
    {
        Categories = await _restaurantService.GetMenuCategoriesAsync(includeInactive: true);
        Items = await _menuService.GetMenuItemsAsync(CategoryId);
    }

    /// <summary>Handles the "Mark available / unavailable" button.</summary>
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

        return RedirectToPage(new { categoryId = CategoryId });
    }

    /// <summary>Handles the Delete button.</summary>
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

        return RedirectToPage(new { categoryId = CategoryId });
    }

    /// <summary>The price a customer pays right now for a dish: the offer price while an offer is running.</summary>
    /// <param name="item">The dish.</param>
    public decimal GetCurrentPrice(MenuItem item)
    {
        return MenuItemPricing.GetCurrentPrice(item, DateTime.UtcNow);
    }

    /// <summary>True while the dish has an offer that is running right now.</summary>
    /// <param name="item">The dish.</param>
    public bool IsOfferActive(MenuItem item)
    {
        return MenuItemPricing.IsPromoActive(item, DateTime.UtcNow);
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
}
