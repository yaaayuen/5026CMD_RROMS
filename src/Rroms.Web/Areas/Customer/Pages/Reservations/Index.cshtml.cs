using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Contracts;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Customer.Pages.Reservations;

/// <summary>
/// "My reservations" (WL-2): the signed-in customer's upcoming and past bookings.
/// </summary>
/// <remarks>
/// Only the signed-in customer's own bookings are loaded, because the ID comes from User.GetUserId().
/// Modify and cancel buttons are added in WL-3.
/// </remarks>
public class IndexModel : PageModel
{
    private readonly IBillingService _billing;
    public ILookup<int, CustomerReceiptLink> Receipts { get; private set; } = Array.Empty<CustomerReceiptLink>().ToLookup(r => r.ReservationId);
    private readonly IReservationService _reservationService;

    /// <summary>Receives the booking service from dependency injection.</summary>
    /// <param name="billing">Reads owned receipt links.</param>
    /// <param name="reservationService">Availability search and booking.</param>
    public IndexModel(IReservationService reservationService, IBillingService billing)
    {
        _reservationService = reservationService;
        _billing = billing;
    }

    /// <summary>Visits that have not finished yet, soonest first.</summary>
    public IReadOnlyList<ReservationSummary> Upcoming { get; private set; } = new List<ReservationSummary>();

    /// <summary>Visits that have finished, most recent first.</summary>
    public IReadOnlyList<ReservationSummary> Past { get; private set; } = new List<ReservationSummary>();

    /// <summary>Handles GET: loads the customer's bookings and splits them into upcoming and past.</summary>
    public async Task OnGetAsync()
    {
        IReadOnlyList<ReservationSummary> all = await _reservationService.GetCustomerReservationsAsync(User.GetUserId());
        Receipts = (await _billing.GetCustomerReceiptLinksAsync(User.GetUserId())).ToLookup(r => r.ReservationId);
        DateTime nowUtc = DateTime.UtcNow;

        Upcoming = all.Where(r => r.EndUtc > nowUtc).OrderBy(r => r.StartUtc).ToList();
        Past = all.Where(r => r.EndUtc <= nowUtc).ToList();
    }

    /// <summary>Bootstrap badge colour for a status, so customers can tell them apart at a glance.</summary>
    /// <param name="status">The reservation's status.</param>
    public static string StatusBadgeClass(ReservationStatus status)
    {
        return Rroms.Web.Common.ReservationStatusDisplay.BadgeClass(status);
    }
}