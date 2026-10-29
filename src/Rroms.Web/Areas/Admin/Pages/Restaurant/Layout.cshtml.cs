using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Restaurant;

/// <summary>
/// Floor plan editor: drag tables into place, turn them, add new ones (Alex, Part 20).
/// /Admin/Restaurant/Layout?areaId=2 edits the Patio. Each save covers one dining area.
/// </summary>
public class LayoutModel : PageModel
{
    /// <summary>The JSON the editor sends and receives (camelCase, like the rest of the site's scripts).</summary>
    public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly IFloorPlanService _floorPlan;

    public LayoutModel(IFloorPlanService floorPlan)
    {
        _floorPlan = floorPlan;
    }

    /// <summary>Address part ?areaId=: the area being edited (the first area when missing or unknown).</summary>
    [BindProperty(SupportsGet = true)]
    public int? AreaId { get; set; }

    /// <summary>The whole layout of the edited area, filled in by floor-plan.js just before the form is sent.</summary>
    [BindProperty]
    public string? LayoutJson { get; set; }

    /// <summary>Every area, for the area tabs.</summary>
    public IReadOnlyList<FloorAreaView> Areas { get; private set; } = new List<FloorAreaView>();

    /// <summary>The area being edited.</summary>
    public FloorAreaView? Area { get; private set; }

    /// <summary>Labels used in the other areas, so new tables get a free label.</summary>
    public string OtherLabels { get; private set; } = string.Empty;

    /// <summary>The edited area as JSON for floor-plan.js.</summary>
    public string AreaJson => JsonSerializer.Serialize(Area, JsonOptions);

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (Area == null)
        {
            return RedirectToPage();
        }

        List<FloorTableInput>? tables = null;
        try
        {
            tables = string.IsNullOrWhiteSpace(LayoutJson) ? null : JsonSerializer.Deserialize<List<FloorTableInput>>(LayoutJson, JsonOptions);
        }
        catch (JsonException)
        {
            // Treated as "nothing sent" below.
        }

        if (tables == null)
        {
            ModelState.AddModelError(string.Empty, FloorPlanService.NothingToSaveMessage);
            return Page();
        }

        ServiceResult result = await _floorPlan.SaveAreaLayoutAsync(Area.Id, tables);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            // Show the layout the administrator was working on, so the problem can be fixed without starting again.
            Area.Tables = tables.Select(t => new FloorTableView
            {
                Id = t.Id, Label = t.Label, Capacity = t.Capacity, X = t.X, Y = t.Y, Rotation = t.Rotation, IsActive = t.IsActive
            }).ToList();
            return Page();
        }

        TempData["StatusMessage"] = $"Layout of {Area.Name} saved.";
        return RedirectToPage(new { areaId = Area.Id });
    }

    private async Task LoadAsync()
    {
        FloorPlanView plan = await _floorPlan.GetFloorPlanAsync(includeInactive: true);
        Areas = plan.Areas;
        Area = plan.Areas.FirstOrDefault(a => a.Id == AreaId) ?? plan.Areas.FirstOrDefault();
        AreaId = Area?.Id;
        OtherLabels = string.Join("|", plan.Areas.Where(a => a.Id != AreaId).SelectMany(a => a.Tables).Select(t => t.Label.ToUpperInvariant()));
    }
}
