using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Restaurant;

/// <summary>
/// Dining areas (for example Main Hall, Patio): list, add and edit. Owner: WL (WL-1).
/// </summary>
/// <remarks>
/// One form does both jobs. /Admin/Restaurant/Areas adds a new area and
/// /Admin/Restaurant/Areas/2 edits area 2. Areas are deactivated instead of deleted,
/// because tables and future bookings point to them.
/// </remarks>
public class AreasModel : PageModel
{
    private readonly IRestaurantService _restaurantService;

    /// <summary>Receives the restaurant service from dependency injection.</summary>
    /// <param name="restaurantService">The restaurant setup service.</param>
    public AreasModel(IRestaurantService restaurantService)
    {
        _restaurantService = restaurantService;
    }

    /// <summary>Every area, including inactive ones, ordered by name.</summary>
    public IReadOnlyList<DiningArea> Areas { get; private set; } = new List<DiningArea>();

    /// <summary>The add/edit form.</summary>
    [BindProperty]
    public DiningAreaInput Input { get; set; } = new DiningAreaInput();

    /// <summary>The area being edited, or null when adding a new one.</summary>
    public int? EditingId { get; private set; }

    /// <summary>Name shown in the form heading while editing.</summary>
    public string EditingName { get; private set; } = string.Empty;

    /// <summary>Handles GET. With an id, fills the form with that area so it can be edited.</summary>
    /// <param name="id">The area to edit, or null to show an empty "add" form.</param>
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id.HasValue)
        {
            DiningArea? area = await _restaurantService.GetDiningAreaAsync(id.Value);
            if (area == null)
            {
                return NotFound();
            }

            EditingId = area.Id;
            EditingName = area.Name;
            Input = new DiningAreaInput
            {
                Name = area.Name,
                Description = area.Description,
                IsActive = area.IsActive
            };
        }

        Areas = await _restaurantService.GetDiningAreasAsync(includeInactive: true);
        return Page();
    }

    /// <summary>Handles POST. Without an id it adds an area, with an id it updates that area.</summary>
    /// <param name="id">The area being edited, or null when adding.</param>
    public async Task<IActionResult> OnPostAsync(int? id)
    {
        EditingId = id;

        if (!ModelState.IsValid)
        {
            Areas = await _restaurantService.GetDiningAreasAsync(includeInactive: true);
            return Page();
        }

        ServiceResult result;
        if (id.HasValue)
        {
            result = await _restaurantService.UpdateDiningAreaAsync(id.Value, Input);
        }
        else
        {
            result = await _restaurantService.CreateDiningAreaAsync(Input);
        }

        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            Areas = await _restaurantService.GetDiningAreasAsync(includeInactive: true);
            return Page();
        }

        TempData["StatusMessage"] = id.HasValue ? "Area updated." : "Area added.";

        // Go back to the plain "add" form so the next save cannot edit the same area by accident.
        return RedirectToPage(new { id = (int?)null });
    }
}