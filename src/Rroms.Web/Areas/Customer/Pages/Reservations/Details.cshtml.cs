using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Customer.Pages.Reservations;

/// <summary>
/// One of the customer's bookings (WL-3), with buttons to change or cancel it while that is still allowed.
/// Notification links point here: /Customer/Reservations/Details/{id}.
/// </summary>
/// <remarks>
/// A booking that does not exist or belongs to someone else returns 404, because the service only
/// finds bookings for the signed-in customer (shared-contracts.md section 3).
/// </remarks>
public class DetailsModel : PageModel
{
    private readonly IReservationService _reservationService;

    /// <summary>Receives the booking service from dependency injection.</summary>
    /// <param name="reservationService">Booking and status changes.</param>
    public DetailsModel(IReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    /// <summary>The booking being shown.</summary>
    public ReservationDetails Booking { get; private set; } = new ReservationDetails();

    /// <summary>True while the customer may still change or cancel: Pending or Confirmed, and not started yet.</summary>
    public bool CanChange { get; private set; }

    /// <summary>
    /// True when the customer may order food for this booking (Confirmed or Seated).
    /// The rule lives only in <see cref="IReservationService.CanOrderForReservationAsync"/>,
    /// so this page, YJ's order page and the service always agree.
    /// </summary>
    public bool CanOrder { get; private set; }

    /// <summary>Handles GET /Customer/Reservations/Details/{id}.</summary>
    /// <param name="id">The booking to show.</param>
    public async Task<IActionResult> OnGetAsync(int id)
    {
        ReservationDetails? details = await _reservationService.GetCustomerReservationAsync(User.GetUserId(), id);
        if (details == null)
        {
            return NotFound();
        }

        Booking = details;
        CanChange = ReservationStatusRules.IsChangeable(details.Status) && details.StartUtc > DateTime.UtcNow;
        CanOrder = (await _reservationService.CanOrderForReservationAsync(User.GetUserId(), id)).Succeeded;
        return Page();
    }

    /// <summary>Handles the Cancel button: cancels the customer's own booking.</summary>
    /// <param name="id">The booking to cancel.</param>
    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        ServiceResult result = await _reservationService.CancelByCustomerAsync(User.GetUserId(), id);
        if (!result.Succeeded)
        {
            TempData["ErrorMessage"] = string.Join(" ", result.Errors);
            return RedirectToPage(new { id });
        }

        TempData["StatusMessage"] = "Your booking has been cancelled. Thank you for letting us know.";
        return RedirectToPage("Index");
    }
}