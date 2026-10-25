using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Admin.Pages.Kitchen;

/// <summary>Kitchen performance for one day: orders, waiting, cooking and serving times, and the busiest hour.</summary>
public class IndexModel : PageModel
{
    private readonly IKitchenInsightsService _insights;
    public IndexModel(IKitchenInsightsService insights) { _insights = insights; }
    /// <summary>The Malaysia day to show; today when empty.</summary>
    [BindProperty(SupportsGet = true)] public DateOnly? Day { get; set; }
    public KitchenStats Stats { get; set; } = new KitchenStats();

    /// <summary>"12.5 min", or a dash when there is nothing to average.</summary>
    public static string Minutes(double? value) => value.HasValue ? value.Value.ToString("0.0") + " min" : "–";

    public async Task OnGetAsync()
    {
        DateOnly today = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
        Day = Day.HasValue && Day.Value <= today ? Day.Value : today;
        Stats = await _insights.GetKitchenStatsAsync(Day.Value);
    }
}
