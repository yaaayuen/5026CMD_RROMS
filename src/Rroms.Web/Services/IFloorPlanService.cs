using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>Reads and saves the restaurant floor plan (Alex, Part 20).</summary>
public interface IFloorPlanService
{
    /// <summary>
    /// The plan of every dining area, with each table's position. Tables without a saved position are put in the
    /// first free spot. With <paramref name="includeInactive"/> false, closed areas and inactive tables are left out.
    /// </summary>
    Task<FloorPlanView> GetFloorPlanAsync(bool includeInactive);

    /// <summary>
    /// Saves one area's layout in a single transaction: positions, rotations, labels, seats and active switches,
    /// plus any new tables (Id 0). Every existing table of the area must be in the list (tables are deactivated,
    /// never deleted). Fails with readable messages when tables overlap, leave the room or reuse a label.
    /// </summary>
    Task<ServiceResult> SaveAreaLayoutAsync(int areaId, IReadOnlyList<FloorTableInput> tables);
}
