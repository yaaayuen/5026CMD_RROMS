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

    /// <summary>True once Darren's DA-3 report has been added with <see cref="AddSales"/>; otherwise the sales charts show a note.</summary>
    public bool SalesAvailable { get; set; }

    /// <summary>Paid sales in RM, one number per day in <see cref="DayLabels"/> (DA-3 daily sales).</summary>
    public IReadOnlyList<decimal> DailySales { get; set; } = new List<decimal>();

    /// <summary>All Paid sales in the range. Equals DA-3's PaidTotal, which is what the reconciliation test checks.</summary>
    public decimal SalesTotal { get; set; }

    /// <summary>Names of the best-selling dishes, most units first.</summary>
    public IReadOnlyList<string> PopularDishNames { get; set; } = new List<string>();

    /// <summary>Units sold of each dish in <see cref="PopularDishNames"/>.</summary>
    public IReadOnlyList<int> PopularDishQuantities { get; set; } = new List<int>();

    /// <summary>How many dishes the popular-dishes chart shows.</summary>
    public const int TopDishCount = 5;

    /// <summary>
    /// Adds Darren's DA-3 sales report for the same range. Nothing is recalculated here: the numbers are copied,
    /// so the dashboard can never disagree with the Reports page.
    /// </summary>
    /// <param name="sales">The DA-3 report for the same From and To.</param>
    public void AddSales(ReportDashboard sales)
    {
        Dictionary<DateOnly, decimal> byDay = sales.DailySales.ToDictionary(d => d.Date, d => d.PaidAmount);
        List<DateOnly> days = new List<DateOnly>();
        for (DateOnly day = From; day <= To; day = day.AddDays(1))
        {
            days.Add(day);
        }

        DailySales = days.Select(day => byDay.TryGetValue(day, out decimal amount) ? amount : 0m).ToList();
        SalesTotal = sales.PaidTotal;

        List<PopularItemReportRow> top = sales.PopularItems.OrderBy(i => i.Rank).Take(TopDishCount).ToList();
        PopularDishNames = top.Select(i => i.Name).ToList();
        PopularDishQuantities = top.Select(i => i.Quantity).ToList();
        SalesAvailable = true;
    }

    /// <summary>
    /// Turns an occupancy report into chart data. Pure and static, so tests can check it without a database.
    /// </summary>
    /// <param name="report">The report for the chosen range.</param>
    public static DashboardChartData FromReport(OccupancyReport report)
    {
        IReadOnlyList<DailyReservationCount> days = report.DailyTrend;

        // Every finished visit ends in one of three outcomes: Completed, Cancelled or Absent.
        List<ChartSeries> byStatus = new List<ChartSeries>
        {
            new ChartSeries("Completed", days.Select(d => d.Completed).ToList()),
            new ChartSeries("Cancelled", days.Select(d => d.Cancelled).ToList()),
            new ChartSeries("Absent", days.Select(d => d.NoShow).ToList())
        };

        // Pending, Confirmed and Seated are not outcomes but visits still to come (only today or later),
        // so they are shown as one series, and only when the range really has some: no number is ever hidden.
        if (days.Any(d => d.NotFinished > 0))
        {
            byStatus.Insert(1, new ChartSeries("Not finished yet", days.Select(d => d.NotFinished).ToList()));
        }

        return new DashboardChartData
        {
            From = report.From,
            To = report.To,
            UtilisationPercent = report.UtilisationPercent,
            DayLabels = days.Select(d => d.Date.ToString("ddd d MMM", CultureInfo.InvariantCulture)).ToList(),
            BookingsByStatus = byStatus,
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
