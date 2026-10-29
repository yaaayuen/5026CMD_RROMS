namespace Rroms.Web.Models;

/// <summary>
/// Where a table stands on its area's floor plan (Alex, Part 20). Kept in its own table so WL's DiningTable does not change.
/// A table without a placement is placed automatically in the first free spot when the plan is shown.
/// </summary>
public class TablePlacement
{
    /// <summary>The table (also the primary key: one placement per table).</summary>
    public int DiningTableId { get; set; }
    public DiningTable? DiningTable { get; set; }
    /// <summary>Left edge of the table's footprint, in grid boxes (1 box = 0.5 m).</summary>
    public int X { get; set; }
    /// <summary>Top edge of the table's footprint, in grid boxes.</summary>
    public int Y { get; set; }
    /// <summary>0 or 90 degrees; 90 turns a long table on its side.</summary>
    public int Rotation { get; set; }
}

/// <summary>The size of a dining area's floor plan, in grid boxes (Alex, Part 20). Kept apart from WL's DiningArea.</summary>
public class FloorArea
{
    /// <summary>The area (also the primary key).</summary>
    public int DiningAreaId { get; set; }
    public DiningArea? DiningArea { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>
/// A fixed part of the room drawn on the plan (Alex, Part 20): a block such as the bar or kitchen that tables
/// must not overlap, or an edge marker such as windows or a door (Width or Height 0).
/// </summary>
public class FloorFeature
{
    public int Id { get; set; }
    public int DiningAreaId { get; set; }
    public DiningArea? DiningArea { get; set; }
    /// <summary>One of <see cref="FloorFeatureKinds"/>.</summary>
    public string Kind { get; set; } = FloorFeatureKinds.Block;
    public string Label { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>The kinds of fixed room features the plan knows how to draw.</summary>
public static class FloorFeatureKinds
{
    /// <summary>A solid block tables cannot overlap (bar, kitchen, planters).</summary>
    public const string Block = "Block";
    /// <summary>Windows along a wall (a line, Height 0).</summary>
    public const string Window = "Window";
    /// <summary>A doorway in a wall (a line, Width or Height 0).</summary>
    public const string Door = "Door";
}
