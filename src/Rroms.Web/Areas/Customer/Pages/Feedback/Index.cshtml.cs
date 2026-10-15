using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models;
using Rroms.Web.Security;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Customer.Pages.Feedback;

/// <summary>Accepts feedback only for the customer's own completed visit.</summary>
public class IndexModel : PageModel
{
    private readonly IFeedbackService _service;
    public IndexModel(IFeedbackService service) { _service = service; }
    [BindProperty] public int ReservationId { get; set; }
    [BindProperty, Range(1, 5)] public int Rating { get; set; } = 5;
    [BindProperty, StringLength(1000)] public string? Comment { get; set; }
    public List<Reservation> Visits { get; set; } = new List<Reservation>();
    public async Task OnGetAsync() { Visits = await _service.GetEligibleAsync(User.GetUserId()); }
    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            var result = await _service.SubmitAsync(User.GetUserId(), ReservationId, Rating, Comment);
            if (result.Succeeded) { TempData["StatusMessage"] = "Thank you for your feedback."; return RedirectToPage(); }
            foreach (string error in result.Errors) { ModelState.AddModelError("", error); }
        }
        await OnGetAsync();
        return Page();
    }
}
