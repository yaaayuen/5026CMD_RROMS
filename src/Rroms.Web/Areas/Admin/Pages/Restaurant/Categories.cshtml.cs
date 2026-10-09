using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Restaurant;

/// <summary>
/// Menu categories (Starters, Grills, ...): list, add and edit. Owner: WL (WL-1).
/// </summary>
/// <remarks>
/// Works like the Areas page: /Admin/Restaurant/Categories adds, /Admin/Restaurant/Categories/3 edits category 3.
/// YJ's menu items read the active categories through <see cref="IRestaurantService.GetMenuCategoriesAsync"/>,
/// so hiding a category here hides it from the customer menu.
/// </remarks>
public class CategoriesModel : PageModel
{
    /// <summary>Gap between suggested display order numbers, so a category can be slotted in between later.</summary>
    private const int DisplayOrderStep = 10;

    private readonly IRestaurantService _restaurantService;

    /// <summary>Receives the restaurant service from dependency injection.</summary>
    /// <param name="restaurantService">The restaurant setup service.</param>
    public CategoriesModel(IRestaurantService restaurantService)
    {
        _restaurantService = restaurantService;
    }

    /// <summary>Every category, including hidden ones, in display order.</summary>
    public IReadOnlyList<MenuCategory> Categories { get; private set; } = new List<MenuCategory>();

    /// <summary>The add/edit form.</summary>
    [BindProperty]
    public MenuCategoryInput Input { get; set; } = new MenuCategoryInput();

    /// <summary>The category being edited, or null when adding a new one.</summary>
    public int? EditingId { get; private set; }

    /// <summary>Name shown in the form heading while editing.</summary>
    public string EditingName { get; private set; } = string.Empty;

    /// <summary>Handles GET. With an id, fills the form with that category so it can be edited.</summary>
    /// <param name="id">The category to edit, or null to show an empty "add" form.</param>
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Categories = await _restaurantService.GetMenuCategoriesAsync(includeInactive: true);

        if (id.HasValue)
        {
            MenuCategory? category = await _restaurantService.GetMenuCategoryAsync(id.Value);
            if (category == null)
            {
                return NotFound();
            }

            EditingId = category.Id;
            EditingName = category.Name;
            Input = new MenuCategoryInput
            {
                Name = category.Name,
                DisplayOrder = category.DisplayOrder,
                IsActive = category.IsActive
            };
        }
        else
        {
            Input.DisplayOrder = SuggestNextDisplayOrder();
        }

        return Page();
    }

    /// <summary>Handles POST. Without an id it adds a category, with an id it updates that category.</summary>
    /// <param name="id">The category being edited, or null when adding.</param>
    public async Task<IActionResult> OnPostAsync(int? id)
    {
        EditingId = id;

        if (!ModelState.IsValid)
        {
            Categories = await _restaurantService.GetMenuCategoriesAsync(includeInactive: true);
            return Page();
        }

        ServiceResult result;
        if (id.HasValue)
        {
            result = await _restaurantService.UpdateMenuCategoryAsync(id.Value, Input);
        }
        else
        {
            result = await _restaurantService.CreateMenuCategoryAsync(Input);
        }

        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            Categories = await _restaurantService.GetMenuCategoriesAsync(includeInactive: true);
            return Page();
        }

        TempData["StatusMessage"] = id.HasValue ? "Category updated." : "Category added.";
        return RedirectToPage(new { id = (int?)null });
    }

    /// <summary>
    /// Suggests the next display order: the highest current number plus the step, capped at the maximum.
    /// </summary>
    private int SuggestNextDisplayOrder()
    {
        if (Categories.Count == 0)
        {
            return DisplayOrderStep;
        }

        int highest = Categories.Max(c => c.DisplayOrder);
        return Math.Min(highest + DisplayOrderStep, MenuCategory.MaxDisplayOrder);
    }
}