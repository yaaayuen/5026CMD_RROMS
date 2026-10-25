using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Contracts;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Staff.Pages.Orders;

/// <summary>
/// Automatic kitchen display: cooking countdowns, the queue and food waiting to be served.
/// Staff only serve, complete or cancel; starting and finishing cooking happen by themselves.
/// The Staff area policy also allows administrators.
/// </summary>
public class IndexModel : PageModel
{
    /// <summary>Queued orders waiting longer than this are highlighted.</summary>
    public const int LongWaitMinutes = 20;
    /// <summary>Cooked food left unserved longer than this is highlighted.</summary>
    public const int LongReadyMinutes = 10;
    private readonly IOrderProcessingService _orders;
    public IndexModel(IOrderProcessingService orders) { _orders = orders; }
    public KitchenBoard Board { get; set; } = new KitchenBoard();

    /// <summary>The moves staff still make by hand. Preparing and Ready to serve are automatic.</summary>
    public static IEnumerable<OrderStatus> StaffMoves(OrderStatus status) =>
        OrderStatusRules.Next(status).Where(s => s is not (OrderStatus.Preparing or OrderStatus.ReadyToServe));

    /// <summary>Whole minutes between two moments, never negative.</summary>
    public static int MinutesBetween(DateTime fromUtc, DateTime toUtc) => Math.Max(0, (int)(toUtc - fromUtc).TotalMinutes);

    public async Task OnGetAsync()
    {
        // Advancing here as well as in KitchenScheduler means the page is never behind the clock.
        await _orders.AdvanceKitchenAsync();
        Board = await _orders.GetKitchenBoardAsync();
    }
    public async Task<IActionResult> OnGetQueueAsync()
    {
        Response.Headers.CacheControl = "no-store";
        await OnGetAsync();
        return Partial("_Queue", this);
    }
    public async Task<IActionResult> OnPostAsync(int id, OrderStatus expected, OrderStatus next)
    {
        if (!ModelState.IsValid) { await OnGetAsync(); return Page(); }
        var result = await _orders.ChangeStatusAsync(id, expected, next);
        if (result.Succeeded)
        {
            // A cancelled order frees its slot, so the next order can start straight away.
            await _orders.AdvanceKitchenAsync();
            return RedirectToPage();
        }
        foreach (string error in result.Errors) { ModelState.AddModelError("", error); }
        await OnGetAsync();
        return Page();
    }
}
