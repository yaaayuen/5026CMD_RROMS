namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// Peak dining hours (WL-4): a grid of weekdays (rows, Monday first) × hours of the day (columns).
/// Each cell holds the occupied table-hours in that hour on that weekday, added up over the whole date range.
/// </summary>
public class OccupancyHeatmap
{
    /// <summary>Row labels: Monday to Sunday.</summary>
    public IReadOnlyList<DayOfWeek> Days { get; set; } = new List<DayOfWeek>();

    /// <summary>Column labels: each hour the restaurant can be open, e.g. 11 to 21 for 11:00–22:00.</summary>
    public IReadOnlyList<int> Hours { get; set; } = new List<int>();

    /// <summary>Cells[day][hour]: occupied table-hours. Same order as <see cref="Days"/> and <see cref="Hours"/>.</summary>
    public double[][] Cells { get; set; } = Array.Empty<double[]>();

    /// <summary>The largest cell value, used to scale the colours (0 when there is no data).</summary>
    public double MaxCell { get; set; }
}
