using System.Globalization;

namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// The numbers the WL-5 Chart.js dashboard draws, shaped as chart labels and series so the browser code stays simple.
/// Sent as JSON by GET /Admin/Analytics?handler=Data&amp;From=...&amp;To=...
/// </summary>
/// <remarks>
/// Built from the same <see cref="OccupancyReport"/> as the tables on the page, so a chart and its table always agree.
/// Sales and popular items come from Darren's DA-3 report service; until it is on main they are not available.
/// </remarks>
public class DashboardChartData
{
    /// <summary>First day of the range.</summary>
    public DateOnly From { get; set; }

    /// <summary>Last day of the range.</summary>
    public DateOnly To { get; set; }

    /// <summary>Table utilisation % (same value and formula as the page).</summary>
    public double UtilisationPercent { get; set; }

    /// <summary>Day labels for the trend chart, e.g. "Mon 5 Oct".</summary>
    public IReadOnlyList<string> DayLabels { get; set; } = new List<string>();

    /// <summary>One series per status, each with one number per day in <see cref="DayLabels"/>.</summary>
    public IReadOnlyList<ChartSeries> BookingsByStatus { get; set; } = new List<ChartSeries>();

    /// <summary>Guests who really dined, one number per day.</summary>
    public IReadOnlyList<int> GuestsServed { get; set; } = new List<int>();

    /// <summary>Hour labels for the peak hours chart, e.g. "19:00".</summary>
    public IReadOnlyList<string> HourLabels { get; set; } = new List<string>();

    /// <summary>Occupied table-hours in each hour, all weekdays added together (the heatmap's column totals).</summary>
    public IReadOnlyList<double> TableHoursByHour { get; set; } = new List<double>();

    /// <summary>False until DA-3 provides sales and popular items; the dashboard then shows a note instead of those charts.</summary>
    public bool SalesAvailable { get; set; }

    /// <summary>
    /// Turns an occupancy report into chart data. Pure and static, so tests can check it without a database.
    /// </summary>
    /// <param name="report">The report for the chosen range.</param>
    public static DashboardChartData FromReport(OccupancyReport report)
    {
        IReadOnlyList<DailyReservationCount> days = report.DailyTrend;

        return new DashboardChartData
        {
            From = report.From,
            To = report.To,
            UtilisationPercent = report.UtilisationPercent,
            DayLabels = days.Select(d => d.Date.ToString("ddd d MMM", CultureInfo.InvariantCulture)).ToList(),
            BookingsByStatus = new List<ChartSeries>
            {
                new ChartSeries("Completed", days.Select(d => d.Completed).ToList()),
                new ChartSeries("Seated", days.Select(d => d.Seated).ToList()),
                new ChartSeries("Confirmed", days.Select(d => d.Confirmed).ToList()),
                new ChartSeries("Pending", days.Select(d => d.Pending).ToList()),
                new ChartSeries("Cancelled", days.Select(d => d.Cancelled).ToList()),
                new ChartSeries("No-show", days.Select(d => d.NoShow).ToList())
            },
            GuestsServed = days.Select(d => d.GuestsServed).ToList(),
            HourLabels = report.Heatmap.Hours.Select(h => $"{h:00}:00").ToList(),
            TableHoursByHour = report.Heatmap.Hours
                .Select((_, column) => report.Heatmap.Cells.Sum(row => row[column]))
                .ToList(),
            SalesAvailable = false
        };
    }
}

/// <summary>One line or bar series: a name and one value per label.</summary>
/// <param name="Name">Legend text, e.g. "Completed".</param>
/// <param name="Values">One value per label, in label order.</param>
public record ChartSeries(string Name, IReadOnlyList<int> Values);
