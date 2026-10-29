using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <inheritdoc cref="IFloorPlanService" />
public class FloorPlanService : IFloorPlanService
{
    public const string AreaNotFoundMessage = "That dining area does not exist.";
    public const string NothingToSaveMessage = "The layout is empty. Reload the page and try again.";
    public const string MissingTableMessage = "Every table must stay on the plan. To stop bookings for a table, untick Active instead of removing it.";
    public const string ForeignTableMessage = "One of the tables belongs to another dining area. Reload the page and try again.";
    public const string ClosedAreaMessage = "This dining area is closed, so new tables cannot be added to it.";
    public const string RotationMessage = "A table can only be turned 0 or 90 degrees.";
    public const string SaveRejectedMessage = "The layout could not be saved because someone else changed the tables at the same moment. Reload the page and try again.";

    private readonly ApplicationDbContext _context;

    public FloorPlanService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<FloorPlanView> GetFloorPlanAsync(bool includeInactive)
    {
        List<DiningArea> areas = await _context.DiningAreas.AsNoTracking()
            .Where(a => includeInactive || a.IsActive)
            .OrderBy(a => a.Id)
            .ToListAsync();
        Dictionary<int, FloorArea> sizes = await _context.FloorAreas.AsNoTracking().ToDictionaryAsync(f => f.DiningAreaId);
        List<FloorFeature> features = await _context.FloorFeatures.AsNoTracking().OrderBy(f => f.Id).ToListAsync();
        List<DiningTable> tables = await _context.DiningTables.AsNoTracking()
            .Where(t => includeInactive || t.IsActive)
            .OrderBy(t => t.Label)
            .ToListAsync();
        Dictionary<int, TablePlacement> placements = await _context.TablePlacements.AsNoTracking().ToDictionaryAsync(p => p.DiningTableId);

        var plan = new FloorPlanView();
        foreach (DiningArea area in areas)
        {
            FloorArea? size = sizes.GetValueOrDefault(area.Id);
            var view = new FloorAreaView
            {
                Id = area.Id,
                Name = area.Name,
                IsActive = area.IsActive,
                Width = size?.Width ?? FloorPlanRules.DefaultWidth,
                Height = size?.Height ?? FloorPlanRules.DefaultHeight,
                Features = features.Where(f => f.DiningAreaId == area.Id).Select(f => new FloorFeatureView
                {
                    Kind = f.Kind, Label = f.Label, X = f.X, Y = f.Y, Width = f.Width, Height = f.Height
                }).ToList()
            };
            List<FloorPlanRules.Obstacle> obstacles = ObstaclesOf(view.Features);

            // Placed tables first, so the unplaced ones are fitted around them.
            List<DiningTable> inArea = tables.Where(t => t.DiningAreaId == area.Id).ToList();
            foreach (DiningTable table in inArea.Where(t => placements.ContainsKey(t.Id)))
            {
                TablePlacement p = placements[table.Id];
                view.Tables.Add(ToView(table, p.X, p.Y, p.Rotation));
            }

            foreach (DiningTable table in inArea.Where(t => !placements.ContainsKey(t.Id)))
            {
                IEnumerable<FloorPlanRules.Box> taken = view.Tables.Select(t => FloorPlanRules.BoxOf(new FloorPlanRules.PlacedTable(t.Label, t.Capacity, t.X, t.Y, t.Rotation)));
                (int x, int y) = FloorPlanRules.FirstFreeSpot(table.Capacity, view.Width, view.Height, obstacles, taken);
                view.Tables.Add(ToView(table, x, y, 0));
            }

            view.Tables = view.Tables.OrderBy(t => t.Label).ToList();
            plan.Areas.Add(view);
        }

        return plan;
    }

    /// <inheritdoc />
    public async Task<ServiceResult> SaveAreaLayoutAsync(int areaId, IReadOnlyList<FloorTableInput> tables)
    {
        DiningArea? area = await _context.DiningAreas.AsNoTracking().SingleOrDefaultAsync(a => a.Id == areaId);
        if (area == null)
        {
            return ServiceResult.Failure(AreaNotFoundMessage);
        }

        if (tables.Count == 0)
        {
            return ServiceResult.Failure(NothingToSaveMessage);
        }

        var errors = new List<string>();
        foreach (FloorTableInput input in tables)
        {
            input.Label = (input.Label ?? string.Empty).Trim();
            if (input.Label.Length == 0 || input.Label.Length > 20)
            {
                errors.Add("Every table needs a label of 1 to 20 characters.");
            }

            if (input.Capacity < DiningTable.MinCapacity || input.Capacity > DiningTable.MaxCapacity)
            {
                errors.Add($"Table {input.Label} must have {DiningTable.MinCapacity} to {DiningTable.MaxCapacity} seats.");
            }

            if (input.Rotation != 0 && input.Rotation != 90)
            {
                errors.Add(RotationMessage);
            }
        }

        List<DiningTable> existing = await _context.DiningTables.Where(t => t.DiningAreaId == areaId).ToListAsync();
        HashSet<int> sentIds = tables.Where(t => t.Id != 0).Select(t => t.Id).ToHashSet();
        if (sentIds.Any(id => existing.All(t => t.Id != id)))
        {
            errors.Add(ForeignTableMessage);
        }

        if (existing.Any(t => !sentIds.Contains(t.Id)))
        {
            errors.Add(MissingTableMessage);
        }

        if (tables.Any(t => t.Id == 0) && !area.IsActive)
        {
            errors.Add(ClosedAreaMessage);
        }

        // Labels are unique across the whole restaurant, ignoring case (SQL Server's default collation does too).
        List<string> otherLabels = await _context.DiningTables.Where(t => t.DiningAreaId != areaId).Select(t => t.Label).ToListAsync();
        var seen = new HashSet<string>(otherLabels, StringComparer.OrdinalIgnoreCase);
        foreach (FloorTableInput input in tables.Where(t => t.Label.Length > 0))
        {
            if (!seen.Add(input.Label))
            {
                errors.Add($"The label {input.Label} is used by another table.");
            }
        }

        if (errors.Count > 0)
        {
            return ServiceResult.Failure(errors.Distinct().ToArray());
        }

        FloorArea? size = await _context.FloorAreas.AsNoTracking().SingleOrDefaultAsync(f => f.DiningAreaId == areaId);
        List<FloorFeatureView> features = await _context.FloorFeatures.AsNoTracking().Where(f => f.DiningAreaId == areaId)
            .Select(f => new FloorFeatureView { Kind = f.Kind, Label = f.Label, X = f.X, Y = f.Y, Width = f.Width, Height = f.Height })
            .ToListAsync();
        // Only active tables take up floor space; an inactive table may sit anywhere (it is shown faded to staff only).
        List<FloorPlanRules.PlacedTable> active = tables.Where(t => t.IsActive)
            .Select(t => new FloorPlanRules.PlacedTable(t.Label, t.Capacity, t.X, t.Y, t.Rotation)).ToList();
        List<string> problems = FloorPlanRules.Problems(area.Name, size?.Width ?? FloorPlanRules.DefaultWidth,
            size?.Height ?? FloorPlanRules.DefaultHeight, ObstaclesOf(features), active);
        if (problems.Count > 0)
        {
            return ServiceResult.Failure(problems.ToArray());
        }

        Dictionary<int, TablePlacement> placements = await _context.TablePlacements
            .Where(p => p.DiningTable!.DiningAreaId == areaId)
            .ToDictionaryAsync(p => p.DiningTableId);
        foreach (FloorTableInput input in tables)
        {
            DiningTable table;
            if (input.Id == 0)
            {
                table = new DiningTable { DiningAreaId = areaId };
                _context.DiningTables.Add(table);
                _context.TablePlacements.Add(new TablePlacement { DiningTable = table, X = input.X, Y = input.Y, Rotation = input.Rotation });
            }
            else
            {
                table = existing.Single(t => t.Id == input.Id);
                if (placements.TryGetValue(table.Id, out TablePlacement? placement))
                {
                    placement.X = input.X;
                    placement.Y = input.Y;
                    placement.Rotation = input.Rotation;
                }
                else
                {
                    _context.TablePlacements.Add(new TablePlacement { DiningTableId = table.Id, X = input.X, Y = input.Y, Rotation = input.Rotation });
                }
            }

            table.Label = input.Label;
            table.Capacity = input.Capacity;
            table.IsActive = input.IsActive;
        }

        // One SaveChanges: the whole layout is saved, or nothing is.
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return ServiceResult.Failure(SaveRejectedMessage);
        }

        return ServiceResult.Success();
    }

    private static List<FloorPlanRules.Obstacle> ObstaclesOf(IEnumerable<FloorFeatureView> features)
    {
        return features.Where(f => f.Kind == FloorFeatureKinds.Block && f.Width > 0 && f.Height > 0)
            .Select(f => new FloorPlanRules.Obstacle(f.Label, new FloorPlanRules.Box(f.X, f.Y, f.Width, f.Height)))
            .ToList();
    }

    private static FloorTableView ToView(DiningTable table, int x, int y, int rotation)
    {
        return new FloorTableView
        {
            Id = table.Id, Label = table.Label, Capacity = table.Capacity, X = x, Y = y, Rotation = rotation, IsActive = table.IsActive
        };
    }
}
