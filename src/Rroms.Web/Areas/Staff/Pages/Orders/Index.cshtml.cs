using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Contracts;
using Rroms.Web.Models;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Staff.Pages.Orders;

/// <summary>Staff kitchen display. The Staff area policy also allows administrators.</summary>
public class IndexModel : PageModel
{
    private readonly IOrderProcessingService _orders;
    public IndexModel(IOrderProcessingService orders) { _orders = orders; }
    [BindProperty(SupportsGet = true)] public OrderStatus? Status { get; set; }
    public List<Order> Orders { get; set; } = new List<Order>();
    public async Task OnGetAsync() { Orders = await _orders.GetKitchenOrdersAsync(Status); }
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
        if (result.Succeeded) { return RedirectToPage(new { Status }); }
        foreach (string error in result.Errors) { ModelState.AddModelError("", error); }
        await OnGetAsync();
        return Page();
    }
}
