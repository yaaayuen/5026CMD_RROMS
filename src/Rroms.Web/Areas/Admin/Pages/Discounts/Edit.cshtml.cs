using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Discounts;

/// <summary>
/// Add or edit one promotion. /Admin/Discounts/Edit adds, /Admin/Discounts/Edit?id=3 edits. Owner: YJ.
/// </summary>
public class EditModel : PageModel
{
    private readonly IDiscountService _discountService;
    private readonly IMenuService _menuService;
    private readonly IRestaurantService _restaurantService;

    /// <summary>Receives the services from dependency injection.</summary>
    /// <param name="discountService">Saves the discount.</param>
    /// <param name="menuService">Gives the dishes to tick.</param>
    /// <param name="restaurantService">Gives the categories to choose.</param>
    public EditModel(IDiscountService discountService, IMenuService menuService, IRestaurantService restaurantService)
    {
        _discountService = discountService;
        _menuService = menuService;
        _restaurantService = restaurantService;
    }

    /// <summary>The form values.</summary>
    [BindProperty]
    public DiscountInput Input { get; set; } = new DiscountInput();

    /// <summary>Every category, for the drop-down.</summary>
    public IReadOnlyList<MenuCategory> Categories { get; private set; } = new List<MenuCategory>();

    /// <summary>Every dish, for the tick boxes.</summary>
    public IReadOnlyList<MenuItem> Dishes { get; private set; } = new List<MenuItem>();

    /// <summary>The dish the live preview uses as its example, or null when the menu is empty.</summary>
    public MenuItem? SampleDish { get; private set; }

    /// <summary>The days shown as tick boxes, Monday first.</summary>
    public IReadOnlyList<DayOfWeek> DayChoices { get; } = new List<DayOfWeek>
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    /// <summary>Handles GET: a blank form, or the discount to edit.</summary>
    /// <param name="id">The discount to edit, or null to add.</param>
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id.HasValue)
        {
            MenuDiscount? discount = await _discountService.GetDiscountAsync(id.Value);

            if (discount == null)
            {
                return NotFound();
            }

            Input = DiscountInput.FromDiscount(discount);
        }

        await LoadChoicesAsync();

        return Page();
    }

    /// <summary>Handles Save.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            ServiceResult<int> result = await _discountService.SaveDiscountAsync(Input);

            if (result.Succeeded)
            {
                TempData["StatusMessage"] = "Promotion saved.";

                return RedirectToPage("/Discounts/Index");
            }

            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
        }

        await LoadChoicesAsync();

        return Page();
    }

    /// <summary>True when the day's box is ticked.</summary>
    /// <param name="day">The day.</param>
    public bool IsDayTicked(DayOfWeek day)
    {
        return Input.SelectedDays.Contains(DiscountDays.Bit(day));
    }

    private async Task LoadChoicesAsync()
    {
        Categories = await _restaurantService.GetMenuCategoriesAsync(includeInactive: true);
        Dishes = await _menuService.GetMenuItemsAsync(null);
        SampleDish = Dishes.FirstOrDefault(dish => dish.IsAvailable) ?? Dishes.FirstOrDefault();
    }
}
