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

    /// <summary>Receives the analytics service from dependency injection.</summary>
    /// <param name="analyticsService">Calculates the occupancy report.</param>
    public IndexModel(IAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    /// <summary>First day of the range (Malaysia date). Empty means four weeks before <see cref="To"/>.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    /// <summary>Last day of the range (Malaysia date). Empty means today.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    /// <summary>The report, or null when the range was refused (the error is in ModelState).</summary>
    public OccupancyReport? Report { get; private set; }

    /// <summary>Handles GET /Admin/Analytics, with or without a date range.</summary>
    public async Task OnGetAsync()
    {
        DateOnly today = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
        To ??= today;
        From ??= To.Value.AddDays(-(DefaultRangeDays - 1));

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
