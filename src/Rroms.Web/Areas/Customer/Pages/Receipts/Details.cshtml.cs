using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Customer.Pages.Receipts;

/// <summary>Displays a paid receipt through the customer access rules.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DetailsModel : PageModel
{
    private readonly IBillingService _billing;
    /// <summary>Receives the billing service.</summary>
    public DetailsModel(IBillingService billing)
    {
        _billing = billing;
    }
    public ReceiptSummary Receipt { get; private set; } = new ReceiptSummary();
    /// <summary>Returns 404 for an unavailable receipt; customer queries also enforce ownership.</summary>
    public async Task<IActionResult> OnGetAsync(int id)
    {
        ReceiptSummary? receipt = await _billing.GetReceiptForCustomerAsync(id, User.GetUserId());
        if (receipt == null)
        {
            return NotFound();
        }
        Receipt = receipt;
        return Page();
    }
}
