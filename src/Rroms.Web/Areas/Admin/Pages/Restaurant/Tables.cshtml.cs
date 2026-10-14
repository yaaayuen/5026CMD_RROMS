using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Restaurant;

/// <summary>
/// Dining tables: list, add and edit, each linked to a dining area. Owner: WL (WL-1).
/// </summary>
/// <remarks>
/// Works like the Areas page: /Admin/Restaurant/Tables adds, /Admin/Restaurant/Tables/3 edits table 3.
/// A table is only offered to customers when the table AND its area are active
/// (the same rule as <see cref="IRestaurantService.GetBookableTablesAsync"/>).
/// </remarks>
public class TablesModel : PageModel
{
    private readonly IRestaurantService _restaurantService;

    /// <summary>Receives the restaurant service from dependency injection.</summary>
    /// <param name="restaurantService">The restaurant setup service.</param>
    public TablesModel(IRestaurantService restaurantService)
    {
        _restaurantService = restaurantService;
    }

    /// <summary>Every table with its area, ordered by area then label.</summary>
    public IReadOnlyList<DiningTable> Tables { get; private set; } = new List<DiningTable>();

    /// <summary>Choices for the "Dining area" drop-down.</summary>
    public List<SelectListItem> AreaOptions { get; private set; } = new List<SelectListItem>();

    /// <summary>The add/edit form.</summary>
    [BindProperty]
    public DiningTableInput Input { get; set; } = new DiningTableInput();

    /// <summary>The table being edited, or null when adding a new one.</summary>
    public int? EditingId { get; private set; }

    /// <summary>Label shown in the form heading while editing.</summary>
    public string EditingLabel { get; private set; } = string.Empty;

    /// <summary>Handles GET. With an id, fills the form with that table so it can be edited.</summary>
    /// <param name="id">The table to edit, or null to show an empty "add" form.</param>
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id.HasValue)
        {
            DiningTable? table = await _restaurantService.GetTableAsync(id.Value);
            if (table == null)
            {
                return NotFound();
            }

            EditingId = table.Id;
            EditingLabel = table.Label;
            Input = new DiningTableInput
            {
                Label = table.Label,
                Capacity = table.Capacity,
                DiningAreaId = table.DiningAreaId,
                IsActive = table.IsActive
            };
        }

        await LoadPageDataAsync();
        return Page();
    }

    /// <summary>Handles POST. Without an id it adds a table, with an id it updates that table.</summary>
    /// <param name="id">The table being edited, or null when adding.</param>
    public async Task<IActionResult> OnPostAsync(int? id)
    {
        EditingId = id;

        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync();
            return Page();
        }

        ServiceResult result;
        if (id.HasValue)
        {
            result = await _restaurantService.UpdateTableAsync(id.Value, Input);
        }
        else
        {
            result = await _restaurantService.CreateTableAsync(Input);
        }

        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            await LoadPageDataAsync();
            return Page();
        }

        TempData["StatusMessage"] = id.HasValue ? "Table updated." : "Table added.";
        return RedirectToPage(new { id = (int?)null });
    }

    /// <summary>True when customers can book this table: the table and its area are both active.</summary>
    /// <param name="table">A table loaded with its area.</param>
    public static bool IsBookable(DiningTable table)
    {
        return table.IsActive && table.DiningArea != null && table.DiningArea.IsActive;
    }

    /// <summary>Loads the table list and the area drop-down, needed whenever the page is shown.</summary>
    private async Task LoadPageDataAsync()
    {
        Tables = await _restaurantService.GetTablesAsync(includeInactive: true);

        IReadOnlyList<DiningArea> areas = await _restaurantService.GetDiningAreasAsync(includeInactive: true);
        AreaOptions = new List<SelectListItem>();
        foreach (DiningArea area in areas)
        {
            string text = area.IsActive ? area.Name : $"{area.Name} (inactive)";
            AreaOptions.Add(new SelectListItem(text, area.Id.ToString()));
        }
    }
}