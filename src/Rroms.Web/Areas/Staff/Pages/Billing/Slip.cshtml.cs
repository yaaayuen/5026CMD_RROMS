using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Staff.Pages.Billing;

/// <summary>
/// The terminal's merchant or customer copy for a card / e-wallet payment.
/// The numbers come from the saved payment, so reprinting always shows the same slip.
/// </summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class SlipModel : PageModel
{
    private readonly IBillingService _billing;

    /// <summary>Receives the billing service.</summary>
    public SlipModel(IBillingService billing)
    {
        _billing = billing;
    }

    public ReceiptSummary Receipt { get; private set; } = new ReceiptSummary();
    public bool IsMerchantCopy { get; private set; }

    /// <summary>Shows one copy of the slip; cash payments have no terminal slip.</summary>
    /// <param name="id">The payment (receipt) ID.</param>
    /// <param name="copy">"merchant" or "customer".</param>
    public async Task<IActionResult> OnGetAsync(int id, string copy)
    {
        ReceiptSummary? receipt = await _billing.GetReceiptForStaffAsync(id);
        if (receipt?.Terminal == null)
        {
            return NotFound();
        }
        Receipt = receipt;
        IsMerchantCopy = copy == "merchant";
        return Page();
    }
}
