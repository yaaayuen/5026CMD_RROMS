using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Customer.Pages.Tracking;

/// <summary>Reads customer records using the signed-in ID, never an ID supplied by a form.</summary>
public class IndexModel : PageModel
{
    private readonly IOrderProcessingService _orders;
    private readonly IReservationService _reservations;
    public IndexModel(IOrderProcessingService orders, IReservationService reservations)
    {
        _orders = orders;
        _reservations = reservations;
    }
    public List<Order> Orders { get; set; } = new List<Order>();
    public IReadOnlyList<ReservationSummary> Reservations { get; set; } = Array.Empty<ReservationSummary>();
    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "no-store";
        Orders = await _orders.GetCustomerOrdersAsync(User.GetUserId());
        Reservations = await _reservations.GetCustomerReservationsAsync(User.GetUserId());
    }
    public async Task<IActionResult> OnGetStatusAsync()
    {
        await OnGetAsync();
        return Partial("_Status", this);
    }
}
