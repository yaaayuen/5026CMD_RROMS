using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Customer.Pages.Tracking;

/// <summary>
/// Reads customer records using the signed-in ID, never an ID supplied by a form.
/// Seated guests can call a staff member or ask for the bill, and finished visits invite a rating.
/// </summary>
public class IndexModel : PageModel
{
    private readonly IOrderProcessingService _orders;
    private readonly IReservationService _reservations;
    private readonly ITableRequestService _requests;
    private readonly IFeedbackService _feedback;
    public IndexModel(IOrderProcessingService orders, IReservationService reservations, ITableRequestService requests, IFeedbackService feedback)
    {
        _orders = orders;
        _reservations = reservations;
        _requests = requests;
        _feedback = feedback;
    }
    /// <summary>Completed visits are offered a rating for this many days.</summary>
    public const int RatingPromptDays = 14;
    public List<Order> Orders { get; set; } = new List<Order>();
    public IReadOnlyList<ReservationSummary> Reservations { get; set; } = Array.Empty<ReservationSummary>();
    /// <summary>The kitchen board, used only to look up countdowns and queue estimates for this customer's own orders.</summary>
    public KitchenBoard Board { get; set; } = new KitchenBoard();
    /// <summary>The customer's open requests, by visit and kind.</summary>
    public List<TableRequest> OpenRequests { get; set; } = new List<TableRequest>();
    /// <summary>Completed visits without feedback yet, newest first.</summary>
    public List<Reservation> Unrated { get; set; } = new List<Reservation>();
    public bool HasOpen(int reservationId, TableRequestKind kind) =>
        OpenRequests.Any(r => r.ReservationId == reservationId && r.Kind == kind);
    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "no-store";
        await _orders.AdvanceKitchenAsync();
        Board = await _orders.GetKitchenBoardAsync();
        Orders = await _orders.GetCustomerOrdersAsync(User.GetUserId());
        Reservations = await _reservations.GetCustomerReservationsAsync(User.GetUserId());
        OpenRequests = await _requests.GetOpenForCustomerAsync(User.GetUserId());
        Unrated = await _feedback.GetEligibleAsync(User.GetUserId());
    }
    public async Task<IActionResult> OnGetStatusAsync()
    {
        await OnGetAsync();
        return Partial("_Status", this);
    }
    public async Task<IActionResult> OnPostRequestAsync(int reservationId, TableRequestKind kind)
    {
        var result = await _requests.RequestAsync(User.GetUserId(), reservationId, kind);
        TempData["StatusMessage"] = !result.Succeeded ? string.Join(" ", result.Errors)
            : kind == TableRequestKind.Bill ? "The staff have been told you would like the bill."
            : "A staff member is on the way to your table.";
        return RedirectToPage();
    }
}
