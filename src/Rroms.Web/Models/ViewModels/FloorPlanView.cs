using System.ComponentModel.DataAnnotations;

namespace Rroms.Web.Models.ViewModels;

/// <summary>Everything a floor plan needs to draw itself (Alex, Part 20). Serialised to JSON for floor-plan.js.</summary>
public class FloorPlanView
{
    public List<FloorAreaView> Areas { get; set; } = new List<FloorAreaView>();
}

/// <summary>One dining area: its size in grid boxes, its fixed features and its tables.</summary>
public class FloorAreaView
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public List<FloorFeatureView> Features { get; set; } = new List<FloorFeatureView>();
    public List<FloorTableView> Tables { get; set; } = new List<FloorTableView>();
}

/// <summary>A bar, kitchen, window or door drawn on the plan.</summary>
public class FloorFeatureView
{
    public string Kind { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>A table on the plan. <see cref="State"/> is only filled on the booking page (Free, Small or Taken).</summary>
public class FloorTableView
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Rotation { get; set; }
    public bool IsActive { get; set; }
    public string? State { get; set; }
}

/// <summary>One table as the layout editor sends it back. Id 0 means a new table.</summary>
public class FloorTableInput
{
    public int Id { get; set; }

    [Required]
    [StringLength(20)]
    public string Label { get; set; } = string.Empty;

    [Range(DiningTable.MinCapacity, DiningTable.MaxCapacity)]
    public int Capacity { get; set; }

    public int X { get; set; }
    public int Y { get; set; }
    public int Rotation { get; set; }
    public bool IsActive { get; set; } = true;
}
