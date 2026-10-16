using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Contracts;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Staff.Pages.Reservations;

/// <summary>
/// Staff daily reservations (WL-3): pick a day, see every booking with its customer and special requests,
/// and move it on with Confirm, Seat, Complete, Cancel or No-show.
/// </summary>
/// <remarks>
/// Only the buttons allowed by <see cref="ReservationStatusRules"/> are shown, and the service checks the move again,
/// so a stale page cannot make an illegal change. Darren's DA-7 can link here or build filters on top.
/// </remarks>
public class IndexModel : PageModel
{
    private readonly IReservationService _reservationService;

    /// <summary>Receives the booking service from dependency injection.</summary>
    /// <param name="reservationService">Daily list and staff status changes (WL-3).</param>
    public IndexModel(IReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    /// <summary>The day being shown (Malaysia date). Empty means today.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly? Date { get; set; }

    /// <summary>That day's bookings, earliest first.</summary>
    public IReadOnlyList<ReservationDetails> Bookings { get; private set; } = new List<ReservationDetails>();

    /// <summary>Handles GET /Staff/Reservations?Date=yyyy-MM-dd.</summary>
    public async Task OnGetAsync()
    {
        Date ??= DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
        Bookings = await _reservationService.GetReservationsForDateAsync(Date.Value);
    }

    /// <summary>Handles a status button: moves one booking to <paramref name="to"/> and comes back to the same day.</summary>
    /// <param name="id">The booking.</param>
    /// <param name="to">The status the button stands for.</param>
    public async Task<IActionResult> OnPostMoveAsync(int id, ReservationStatus to)
    {
        ServiceResult result = to switch
        {
            ReservationStatus.Confirmed => await _reservationService.ConfirmAsync(id),
            ReservationStatus.Seated => await _reservationService.SeatAsync(id),
            ReservationStatus.Completed => await _reservationService.CompleteAsync(id),
            ReservationStatus.Cancelled => await _reservationService.CancelByStaffAsync(id),
            ReservationStatus.NoShow => await _reservationService.MarkNoShowAsync(id),
            _ => ServiceResult.Failure($"A booking cannot be moved to {to} here.")
        };

        if (result.Succeeded)
        {
            TempData["StatusMessage"] = $"Booking #{id} is now {to}.";
        }
        else
        {
            TempData["ErrorMessage"] = string.Join(" ", result.Errors);
        }

        return RedirectToPage(new { Date = Date?.ToString("yyyy-MM-dd") });
    }
}
