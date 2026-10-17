using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Admin.Pages.Monitoring;
/// <summary>DA-2 reservations; the existing Admin area policy restricts access to administrators.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ReservationsModel : PageModel
{
    private readonly IMonitoringService _monitoring;
    /// <summary>Receives read-only monitoring queries.</summary>
    public ReservationsModel(IMonitoringService monitoring)
    {
        _monitoring = monitoring;
    }
    [BindProperty(SupportsGet = true)]
    public ReservationMonitoringFilter Filter { get; set; } = new ReservationMonitoringFilter();
    public ReservationMonitoringResult Results { get; private set; } = new ReservationMonitoringResult();
    public bool Loaded { get; private set; }
    public IReadOnlyList<ReservationAreaOption> Areas { get; private set; } = Array.Empty<ReservationAreaOption>();
    /// <summary>Checks binding errors and service validation before showing records or totals.</summary>
    public async Task OnGetAsync()
    {
        Areas = await _monitoring.GetReservationAreasAsync();
        if (!ModelState.IsValid) { return; }
        var result = await _monitoring.GetReservationMonitoringAsync(Filter);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors) { ModelState.AddModelError(string.Empty, error); }
            return;
        }
        Results = result.Value!;
        Loaded = true;
    }
}
