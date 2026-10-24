using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Staff.Pages.Billing;
public class IndexModel(IBillingService billing) : PageModel
{
    [BindProperty(SupportsGet = true)] public string View { get; set; } = "unpaid";
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public BillingDashboard Dashboard { get; private set; } = new();
    public async Task OnGetAsync() {
        if (View != "all" && View != "paid") View = "unpaid";
        Dashboard = await billing.GetDashboardAsync(View, Search, PageNumber);
    }
}
