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
    private readonly ReceiptDocumentService _documents;
    private readonly IBillingService _billing;
    /// <summary>Receives the billing service.</summary>
    public ReceiptModel(IBillingService billing, ReceiptDocumentService documents)
    {
        _billing = billing;
        _documents = documents;
    }
    [BindProperty] public string? RefundReason { get; set; }
    [BindProperty] public bool MoneyReturned { get; set; }
    /// <summary>The dishes and quantities chosen for a dish refund. Amounts are calculated by the billing service.</summary>
    [BindProperty] public List<RefundLineInput> RefundLines { get; set; } = new();
    public async Task<IActionResult> OnPostRefundAsync(int id) => await FinishRefundAsync(id, false);
    public async Task<IActionResult> OnPostRejectRefundAsync(int id) => await FinishRefundAsync(id, true);
    private async Task<IActionResult> FinishRefundAsync(int id, bool reject)
    {
        var result = reject ? await _billing.RejectRefundAsync(id, RefundReason, User.GetUserId())
            : await _billing.RecordRefundAsync(id, RefundReason, User.GetUserId(), MoneyReturned,
                RefundLines.Count == 0 ? null : RefundLines);
        if(!result.Succeeded) {
            foreach(var error in result.Errors) ModelState.AddModelError(string.Empty, error);
            return await OnGetAsync(id);
        }
        TempData["ReceiptMessage"] = (reject ? "Refund request rejected." : "Refund recorded. Do not return the money again.")
            + (result.Value!.NotificationSent ? "" : " Customer notification failed.");
        return RedirectToPage(new { id });
    }
    [BindProperty] public string? VoidReason { get; set; }
    public async Task<IActionResult> OnPostVoidAsync(int id)
    {
        if(!User.IsInRole(Rroms.Web.Constants.RoleNames.Administrator)) return Forbid();
        var result = await _billing.VoidPaymentAsync(id, VoidReason, User.GetUserId());
        if(!result.Succeeded) {
            foreach(var error in result.Errors) ModelState.AddModelError(string.Empty, error);
            return await OnGetAsync(id);
        }
        TempData["ReceiptMessage"] = result.Value!.NotificationSent ? "Receipt voided. The order can be paid again." : "Receipt voided, but the customer notification failed. Do not void it again.";
        return RedirectToPage(new { id });
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
    public async Task<IActionResult> OnGetPdfAsync(int id)
    {
        var receipt = await _billing.GetReceiptForStaffAsync(id);
        if (receipt == null) return NotFound();
        return File(_documents.Pdf(receipt), "application/pdf", $"{receipt.Number}.pdf");
    }
    public async Task<IActionResult> OnPostEmailAsync(int id)
    {
        var receipt = await _billing.GetReceiptForStaffAsync(id);
        if(receipt == null) return NotFound();
        TempData["ReceiptMessage"] = _documents.QueueEmail(receipt)
            ? "Receipt email queued for the customer's registered email address."
            : "Receipt email could not be queued. Check the customer's email address or try again later.";
        return RedirectToPage(new { id });
    }
}
