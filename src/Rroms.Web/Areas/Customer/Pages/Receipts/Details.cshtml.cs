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
    private readonly ReceiptDocumentService _documents;
    private readonly IBillingService _billing;
    /// <summary>Receives the billing service.</summary>
    public DetailsModel(IBillingService billing, ReceiptDocumentService documents)
    {
        _billing = billing;
        _documents = documents;
    }
    [BindProperty] public string? RefundReason { get; set; }
    public async Task<IActionResult> OnPostRequestRefundAsync(int id)
    {
        if(await _billing.GetReceiptForCustomerAsync(id, User.GetUserId()) == null) return NotFound();
        var result = await _billing.RequestRefundAsync(id, RefundReason, User.GetUserId());
        if(!result.Succeeded) {
            foreach(var error in result.Errors) ModelState.AddModelError(string.Empty, error);
            return await OnGetAsync(id);
        }
        TempData["RefundMessage"] = "Refund request submitted. Staff will review it; no money has been returned yet."
            + (result.Value!.NotificationSent ? "" : " Confirmation notification could not be sent.");
        return RedirectToPage(new { id });
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
    public async Task<IActionResult> OnGetPdfAsync(int id)
    {
        var receipt = await _billing.GetReceiptForCustomerAsync(id, User.GetUserId());
        if (receipt == null) return NotFound();
        return File(_documents.Pdf(receipt), "application/pdf", $"{receipt.Number}.pdf");
    }
}
