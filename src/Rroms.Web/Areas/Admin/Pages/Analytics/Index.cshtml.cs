using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages.Analytics;

/// <summary>
/// Table occupancy and reservation analytics for the administrator (WL-4): utilisation %, peak hours heatmap,
/// daily reservation trend and upcoming confirmed bookings for a chosen date range.
/// </summary>
/// <remarks>
/// The filter uses GET, so a range can be bookmarked or shared as a link, e.g. /Admin/Analytics?From=2026-10-01&amp;To=2026-10-31.
/// All numbers come from <see cref="IAnalyticsService"/>; this page only shows them.
/// </remarks>
public class IndexModel : PageModel
{
    /// <summary>How many days the page shows when no range is chosen (the last four weeks, today included).</summary>
    public const int DefaultRangeDays = 28;

    private readonly IAnalyticsService _analyticsService;
    private readonly IReportService _reportService;

    /// <summary>Receives the services from dependency injection.</summary>
    /// <param name="analyticsService">Calculates the occupancy report (WL-4).</param>
    /// <param name="reportService">Darren's sales and popular-items report (DA-3), so sales figures match his Reports page.</param>
    public IndexModel(IAnalyticsService analyticsService, IReportService reportService)
    {
        _analyticsService = analyticsService;
        _reportService = reportService;
    }

    /// <summary>First day of the range (Malaysia date). Empty means four weeks before <see cref="To"/>.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    /// <summary>Last day of the range (Malaysia date). Empty means today.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    /// <summary>The report, or null when the range was refused (the error is in ModelState).</summary>
    public OccupancyReport? Report { get; private set; }

    /// <summary>DA-3 sales for the same range, or null if the sales report could not be built.</summary>
    public ReportDashboard? Sales { get; private set; }

    /// <summary>The next 7 days: booked so far against the usual for that weekday (simple forecast).</summary>
    public IReadOnlyList<DayOutlook> Outlook { get; private set; } = new List<DayOutlook>();

    /// <summary>Handles GET /Admin/Analytics, with or without a date range.</summary>
    public async Task OnGetAsync()
    {
        ApplyDefaultRange();

        ServiceResult<OccupancyReport> result = await _analyticsService.GetOccupancyReportAsync(From.Value, To.Value);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return;
        }

        Report = result.Value;
        Outlook = await _analyticsService.GetOutlookAsync(DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow)));
        Sales = await GetSalesAsync();
    }

    /// <summary>
    /// Handles GET /Admin/Analytics?handler=Data&amp;From=...&amp;To=... for the Chart.js dashboard (WL-5).
    /// Returns the same range's numbers as JSON, or 400 with the error messages when the range is refused.
    /// </summary>
    public async Task<IActionResult> OnGetDataAsync()
    {
        ApplyDefaultRange();

        ServiceResult<OccupancyReport> result = await _analyticsService.GetOccupancyReportAsync(From.Value, To.Value);
        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors });
        }

        DashboardChartData data = DashboardChartData.FromReport(result.Value!);
        ReportDashboard? sales = await GetSalesAsync();
        if (sales != null)
        {
            data.AddSales(sales);
        }

        return new JsonResult(data);
    }

    /// <summary>
    /// Asks Darren's report service for the same From–To range. Sales are never calculated here,
    /// so the dashboard and the Reports page always show the same totals.
    /// </summary>
    private async Task<ReportDashboard?> GetSalesAsync()
    {
        ServiceResult<ReportDashboard> sales = await _reportService.GetReportAsync(new ReportFilter
        {
            Period = ReportPeriod.Custom,
            From = From!.Value,
            To = To!.Value
        });

        return sales.Succeeded ? sales.Value : null;
    }

    /// <summary>Fills in an empty range: To defaults to today and From to four weeks before it (Malaysia dates).</summary>
    private void ApplyDefaultRange()
    {
        DateOnly today = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
        To ??= today;
        From ??= To.Value.AddDays(-(DefaultRangeDays - 1));
    }

    /// <summary>
    /// Background colour for one heatmap cell: the busiest cell is solid raspberry, an empty cell is transparent,
    /// and everything else is in between, so the busy hours stand out at a glance.
    /// </summary>
    /// <param name="value">The cell's occupied table-hours.</param>
    /// <param name="max">The largest cell in the grid.</param>
    public static string HeatColour(double value, double max)
    {
        double strength = max > 0 ? value / max : 0;
        return $"rgba(216, 17, 89, {strength.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)})";
    }

    /// <summary>Text colour that stays readable on <see cref="HeatColour"/>: white on dark cells, dark on light ones.</summary>
    public static string HeatTextColour(double value, double max)
    {
        return max > 0 && value / max > 0.55 ? "#ffffff" : "var(--tg-ink)";
    }
}
