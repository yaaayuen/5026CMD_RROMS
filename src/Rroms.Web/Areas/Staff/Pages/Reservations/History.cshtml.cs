using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Staff.Pages.Reservations;
/// <summary>DA-7 daily reservations. Access is enforced by the existing Staff area policy.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class HistoryModel : PageModel
{
    private readonly IMonitoringService _monitoring;
    /// <summary>Receives the monitoring queries through dependency injection.</summary>
    public HistoryModel(IMonitoringService monitoring)
    {
        _monitoring = monitoring;
    }
    [BindProperty(SupportsGet = true)]
    public DailyReservationFilter Filter { get; set; } = new DailyReservationFilter();
    public IReadOnlyList<ReservationAreaOption> Areas { get; private set; } = Array.Empty<ReservationAreaOption>();
    public IReadOnlyList<StaffReservationRow> Rows { get; private set; } = Array.Empty<StaffReservationRow>();
    public bool Loaded { get; private set; }
    /// <summary>Validates incoming filters before loading any customer information.</summary>
    public async Task OnGetAsync()
    {
        Areas = await _monitoring.GetReservationAreasAsync();
        if (!ModelState.IsValid)
        {
            return;
        }
        ServiceResult<IReadOnlyList<StaffReservationRow>> result = await _monitoring.GetDailyReservationsAsync(Filter);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            return;
        }
        Rows = result.Value!;
        Loaded = true;
    }
}
