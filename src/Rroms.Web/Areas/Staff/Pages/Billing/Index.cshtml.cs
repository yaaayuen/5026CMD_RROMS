using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Staff.Pages.Billing;

/// <summary>Completed orders ready for billing, with an option to find paid receipts.</summary>
public class IndexModel : PageModel
{
    private readonly IBillingService _billing;
    /// <summary>Receives the billing service.</summary>
    public IndexModel(IBillingService billing)
    {
        _billing = billing;
    }
    [BindProperty(SupportsGet = true)]
    public bool IncludePaid { get; set; }
    public IReadOnlyList<BillSummary> Bills { get; private set; } = Array.Empty<BillSummary>();
    /// <summary>Reads completed orders through the service.</summary>
    public async Task OnGetAsync()
    {
        Bills = await _billing.GetCompletedOrdersAsync(IncludePaid);
    }
}
