using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Staff.Pages.Billing;

/// <summary>
/// One bill for a whole visit (all unpaid orders of a reservation): the customer pays once and gets one receipt.
/// </summary>
public class VisitModel : PageModel
{
    private readonly IBillingService _billing;

    /// <summary>Receives the billing service.</summary>
    public VisitModel(IBillingService billing)
    {
        _billing = billing;
    }

    public VisitBill Visit { get; private set; } = new VisitBill();

    [BindProperty]
    public RecordPaymentInput Input { get; set; } = new RecordPaymentInput();

    /// <summary>Shows the visit's bill; displaying it never creates a payment.</summary>
    /// <param name="id">The reservation (visit).</param>
    public async Task<IActionResult> OnGetAsync(int id)
    {
        VisitBill? visit = await _billing.GetVisitBillAsync(id);
        if (visit == null)
        {
            return NotFound();
        }
        Visit = visit;
        return Page();
    }

    /// <summary>Records one payment for every unpaid order of the visit, then opens the combined receipt.</summary>
    /// <param name="id">The reservation (visit).</param>
    public async Task<IActionResult> OnPostAsync(int id)
    {
        VisitBill? visit = await _billing.GetVisitBillAsync(id);
        if (visit == null)
        {
            return NotFound();
        }
        Visit = visit;
        if (!ModelState.IsValid)
        {
            return Page();
        }
        ServiceResult<PaymentRecordingResult> result = await _billing.RecordVisitPaymentAsync(
            id, Input.Method!.Value, User.GetUserId());
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            Visit = await _billing.GetVisitBillAsync(id) ?? visit;
            return Page();
        }
        if (!result.Value!.NotificationSent)
        {
            TempData["BillingWarning"] = BillingService.NotificationWarning;
        }
        return RedirectToPage("Receipt", new { id = result.Value.PaymentId });
    }
}
