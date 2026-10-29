namespace Rroms.Web.Services;

/// <summary>
/// Geometry of the floor plan (Alex, Part 20), in grid boxes (1 box = 0.5 m). The browser editor (floor-plan.js)
/// uses the same numbers, but the server checks every save with these rules.
/// </summary>
public static class FloorPlanRules
{
    /// <summary>Clear boxes needed between two tables so guests can walk through.</summary>
    public const int Gap = 1;

    /// <summary>Plan size used for an area that has no saved size yet.</summary>
    public const int DefaultWidth = 24;
    public const int DefaultHeight = 15;

    /// <summary>A rectangle on the grid.</summary>
    public readonly record struct Box(int X, int Y, int Width, int Height);

    /// <summary>A table to check: where it is and how big.</summary>
    public readonly record struct PlacedTable(string Label, int Capacity, int X, int Y, int Rotation);

    /// <summary>A block the tables must not overlap (bar, kitchen, planters).</summary>
    public readonly record struct Obstacle(string Label, Box Box);

    /// <summary>
    /// The space a table takes, chairs included: round 3x3 for up to 2 seats, square 4x4 for up to 4,
    /// otherwise 4 deep and 2 + seats/2 long. A rotation of 90 turns it on its side.
    /// </summary>
    public static (int Width, int Height) Footprint(int capacity, int rotation)
    {
        (int w, int h) = capacity <= 2 ? (3, 3) : capacity <= 4 ? (4, 4) : (2 + (capacity + 1) / 2, 4);
        return rotation == 90 ? (h, w) : (w, h);
    }

    public static Box BoxOf(PlacedTable table)
    {
        (int w, int h) = Footprint(table.Capacity, table.Rotation);
        return new Box(table.X, table.Y, w, h);
    }

    /// <summary>True when the boxes overlap, or come closer than <paramref name="gap"/> boxes.</summary>
    public static bool Overlaps(Box a, Box b, int gap = 0)
    {
        return a.X < b.X + b.Width + gap && b.X < a.X + a.Width + gap
            && a.Y < b.Y + b.Height + gap && b.Y < a.Y + a.Height + gap;
    }

    /// <summary>
    /// Every problem with a layout: tables past the walls, tables on the bar or kitchen, and tables closer than
    /// one box to each other. Tables may touch a wall or a feature. An empty list means the layout is valid.
    /// </summary>
    public static List<string> Problems(string areaName, int width, int height, IReadOnlyList<Obstacle> obstacles, IReadOnlyList<PlacedTable> tables)
    {
        var problems = new List<string>();
        for (int i = 0; i < tables.Count; i++)
        {
            PlacedTable table = tables[i];
            Box box = BoxOf(table);
            if (box.X < 0 || box.Y < 0 || box.X + box.Width > width || box.Y + box.Height > height)
            {
                problems.Add($"Table {table.Label} goes past the edge of {areaName}.");
            }

            foreach (Obstacle obstacle in obstacles)
            {
                if (Overlaps(box, obstacle.Box))
                {
                    problems.Add($"Table {table.Label} overlaps the {obstacle.Label.ToLowerInvariant()}.");
                }
            }

            for (int j = i + 1; j < tables.Count; j++)
            {
                if (Overlaps(box, BoxOf(tables[j]), Gap))
                {
                    problems.Add($"Tables {table.Label} and {tables[j].Label} need at least one clear box between them.");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// The first free spot for a table, scanning row by row from the top left, or (0, 0) when the area is full.
    /// Used for tables that were added on the Tables page and have no position yet.
    /// </summary>
    public static (int X, int Y) FirstFreeSpot(int capacity, int width, int height, IReadOnlyList<Obstacle> obstacles, IEnumerable<Box> taken)
    {
        (int w, int h) = Footprint(capacity, 0);
        List<Box> others = taken.ToList();
        for (int y = 0; y + h <= height; y++)
        {
            for (int x = 0; x + w <= width; x++)
            {
                var box = new Box(x, y, w, h);
                if (!obstacles.Any(o => Overlaps(box, o.Box)) && !others.Any(o => Overlaps(box, o, Gap)))
                {
                    return (x, y);
                }
            }
        }

        return (0, 0);
    }
}
