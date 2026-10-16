using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Staff.Pages.Reservations;
/// <summary>Staff-only customer history selected through an existing reservation.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CustomerHistoryModel : PageModel
{
    private readonly IMonitoringService _monitoring;
    /// <summary>Receives staff history queries.</summary>
    public CustomerHistoryModel(IMonitoringService monitoring)
    {
        _monitoring = monitoring;
    }
    public StaffReservationHistory History { get; private set; } = new StaffReservationHistory();
    /// <summary>Returns 404 for missing/invalid bookings, otherwise shows that customer's past reservations.</summary>
    public async Task<IActionResult> OnGetAsync(int reservationId)
    {
        if (!ModelState.IsValid || reservationId <= 0)
        {
            return NotFound();
        }
        StaffReservationHistory? history = await _monitoring.GetCustomerHistoryAsync(reservationId);
        if (history == null)
        {
            return NotFound();
        }
        History = history;
        return Page();
    }
}
