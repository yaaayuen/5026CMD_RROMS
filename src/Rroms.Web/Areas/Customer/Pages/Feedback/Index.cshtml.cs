using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models;
using Rroms.Web.Security;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Customer.Pages.Feedback;

/// <summary>
/// Accepts feedback only for the customer's own completed visit, with optional stars for the dishes from that visit,
/// and lists their past reviews with any reply.
/// </summary>
public class IndexModel : PageModel
{
    private readonly IFeedbackService _service;
    public IndexModel(IFeedbackService service) { _service = service; }
    /// <summary>The visit being rated. A link from the tracking page can choose it, with the stars.</summary>
    [BindProperty(SupportsGet = true)] public int ReservationId { get; set; }
    [BindProperty(SupportsGet = true), Range(1, 5)] public int Rating { get; set; } = 5;
    [BindProperty, StringLength(1000)] public string? Comment { get; set; }
    /// <summary>Stars by dish id. A dish left empty is not rated.</summary>
    [BindProperty] public Dictionary<int, int?> DishRatings { get; set; } = new Dictionary<int, int?>();
    public List<Reservation> Visits { get; set; } = new List<Reservation>();
    public List<MenuItem> Dishes { get; set; } = new List<MenuItem>();
    public List<Models.Feedback> Past { get; set; } = new List<Models.Feedback>();
    public async Task OnGetAsync()
    {
        Visits = await _service.GetEligibleAsync(User.GetUserId());
        Past = await _service.GetMineAsync(User.GetUserId());
        // An unknown or already-rated visit falls back to the newest one waiting for feedback.
        if (!Visits.Any(v => v.Id == ReservationId)) { ReservationId = Visits.FirstOrDefault()?.Id ?? 0; }
        if (Rating is < 1 or > 5)
        {
            ModelState.Remove(nameof(Rating));
            Rating = 5;
        }
        if (ReservationId != 0) { Dishes = await _service.GetVisitDishesAsync(User.GetUserId(), ReservationId); }
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            var dishes = DishRatings.Where(d => d.Value.HasValue).ToDictionary(d => d.Key, d => d.Value!.Value);
            var result = await _service.SubmitAsync(User.GetUserId(), ReservationId, Rating, Comment, dishes);
            if (result.Succeeded) { TempData["StatusMessage"] = "Thank you for your feedback."; return RedirectToPage(); }
            foreach (string error in result.Errors) { ModelState.AddModelError("", error); }
        }
        await OnGetAsync();
        return Page();
    }
}
