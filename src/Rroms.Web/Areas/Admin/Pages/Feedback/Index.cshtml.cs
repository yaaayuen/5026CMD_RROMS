using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Common;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Admin.Pages.Feedback;

/// <summary>Filters reviews and calculates the average for the displayed results.</summary>
public class IndexModel : PageModel
{
    private readonly IFeedbackService _service;
    public IndexModel(IFeedbackService service) { _service = service; }
    [BindProperty(SupportsGet = true)] public int? Rating { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? Until { get; set; }
    public List<Models.Feedback> Items { get; set; } = new List<Models.Feedback>();
    public double? Average => Items.Count == 0 ? null : Items.Average(f => f.Rating);
    public async Task OnGetAsync()
    {
        if (!ModelState.IsValid) { return; }
        if (From > Until) { ModelState.AddModelError("", "End date must not precede start date."); return; }
        Items = await _service.GetAsync(Rating, From.HasValue ? MalaysiaTime.ToUtc(From.Value.Date) : null,
            Until.HasValue ? MalaysiaTime.ToUtc(Until.Value.Date.AddDays(1)) : null);
    }
}
