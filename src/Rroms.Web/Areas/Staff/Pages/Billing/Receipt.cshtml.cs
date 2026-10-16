using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Staff.Pages.Billing;

/// <summary>Displays a paid receipt through the staff access rules.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ReceiptModel : PageModel
{
    private readonly IBillingService _billing;
    /// <summary>Receives the billing service.</summary>
    public ReceiptModel(IBillingService billing)
    {
        _billing = billing;
    }
    public ReceiptSummary Receipt { get; private set; } = new ReceiptSummary();
    /// <summary>Returns 404 for an unavailable receipt; customer queries also enforce ownership.</summary>
    public async Task<IActionResult> OnGetAsync(int id)
    {
        ReceiptSummary? receipt = await _billing.GetReceiptForStaffAsync(id);
        if (receipt == null)
        {
            return NotFound();
        }
        Receipt = receipt;
        return Page();
    }
}
