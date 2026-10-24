using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Staff.Pages.Billing;

/// <summary>Displays a bill and records a payment using trusted order and staff data.</summary>
public class DetailsModel : PageModel
{
    private readonly IBillingService _billing;
    /// <summary>Receives the billing service.</summary>
    public DetailsModel(IBillingService billing)
    {
        _billing = billing;
    }
    public BillSummary Bill { get; private set; } = new BillSummary();
    [BindProperty]
    public RecordPaymentInput Input { get; set; } = new RecordPaymentInput();
    /// <summary>Generates the bill view; displaying it never creates a payment.</summary>
    public async Task<IActionResult> OnGetAsync(int id)
    {
        BillSummary? bill = await _billing.GetBillAsync(id);
        if (bill == null)
        {
            return NotFound();
        }
        Bill = bill;
        return Page();
    }
    /// <summary>Validates the form and records payment; Razor Pages checks its antiforgery token.</summary>
    public async Task<IActionResult> OnPostAsync(int id)
    {
        BillSummary? bill = await _billing.GetBillAsync(id);
        if (bill == null)
        {
            return NotFound();
        }
        Bill = bill;
        if (!ModelState.IsValid)
        {
            return Page();
        }
        ServiceResult<PaymentRecordingResult> result = await _billing.RecordPaymentAsync(
            id, Input.Method!.Value, User.GetUserId(), Input.CashReceived);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }
            // Another request might have paid while this form was open. Show its receipt link.
            Bill = await _billing.GetBillAsync(id) ?? bill;
            return Page();
        }
        if (!result.Value!.NotificationSent)
        {
            TempData["BillingWarning"] = BillingService.NotificationWarning;
        }
        return RedirectToPage("Receipt", new { id = result.Value.PaymentId });
    }
}
