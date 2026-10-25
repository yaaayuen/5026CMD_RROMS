using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Admin.Pages.Announcements;

/// <summary>Creates, edits, schedules, repeats and unpublishes announcements, grouped by Upcoming, Live, Drafts and Ended.</summary>
public class IndexModel : PageModel
{
    private readonly IAnnouncementService _service;
    public IndexModel(IAnnouncementService service) { _service = service; }
    [BindProperty] public Announcement Input { get; set; } = new Announcement();
    [BindProperty] public DateTime StartLocal { get; set; } = MalaysiaTime.FromUtc(DateTime.UtcNow);
    [BindProperty] public DateTime EndLocal { get; set; } = MalaysiaTime.FromUtc(DateTime.UtcNow).AddDays(7);
    /// <summary>Days a recurring post shows on. None ticked means a one-off post.</summary>
    [BindProperty] public List<DayOfWeek> RepeatOn { get; set; } = new List<DayOfWeek>();
    /// <summary>Monday first, as the week is shown in Malaysia.</summary>
    public static readonly DayOfWeek[] WeekOrder = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
    /// <summary>The tab from the query string: "upcoming", "live", "drafts", "ended", or anything else for all.</summary>
    [BindProperty(SupportsGet = true)] public string? Show { get; set; }
    public List<Announcement> Items { get; set; } = new List<Announcement>();
    public List<SelectListItem> Dishes { get; set; } = new List<SelectListItem>();
    public Dictionary<string, int> Counts { get; set; } = new Dictionary<string, int>();

    /// <summary>The tab an announcement belongs to at the given moment. A recurring post between its showings is upcoming.</summary>
    public static string StateOf(Announcement item, DateTime nowUtc)
    {
        if (!item.IsPublished) { return "drafts"; }
        if (item.EndUtc <= nowUtc) { return "ended"; }
        if (AnnouncementSchedule.IsShowing(item, nowUtc)) { return "live"; }
        return AnnouncementSchedule.NextStartUtc(item, nowUtc) == null ? "ended" : "upcoming";
    }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        await LoadAsync();
        if (id.HasValue)
        {
            var item = (await _service.GetAsync(false)).SingleOrDefault(a => a.Id == id.Value);
            if (item == null) { return NotFound(); }
            Input = item;
            StartLocal = MalaysiaTime.FromUtc(item.StartUtc);
            EndLocal = MalaysiaTime.FromUtc(item.EndUtc);
            RepeatOn = WeekOrder.Where(d => (item.RepeatDays & DiscountDays.Bit(d)) != 0).ToList();
        }
        return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            Input.StartUtc = MalaysiaTime.ToUtc(StartLocal);
            Input.EndUtc = MalaysiaTime.ToUtc(EndLocal);
            Input.RepeatDays = RepeatOn.Distinct().Aggregate(0, (mask, day) => mask | DiscountDays.Bit(day));
            var result = await _service.SaveAsync(Input);
            if (result.Succeeded)
            {
                bool scheduled = Input.IsPromotion && Input.IsPublished && Input.StartUtc > DateTime.UtcNow;
                TempData["StatusMessage"] = Input.IsPromotion && Input.IsPublished && Input.RepeatDays != 0
                    ? "Recurring announcement saved. Customers are notified each time it starts showing."
                    : scheduled ? "Announcement scheduled. Customers are notified when it starts." : "Announcement saved.";
                return RedirectToPage();
            }
            foreach (string error in result.Errors) { ModelState.AddModelError("", error); }
        }
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        DateTime now = DateTime.UtcNow;
        List<Announcement> all = await _service.GetAsync(false);
        Counts = all.GroupBy(a => StateOf(a, now)).ToDictionary(g => g.Key, g => g.Count());
        Show = Show is "upcoming" or "live" or "drafts" or "ended" ? Show : null;
        // Upcoming posts are listed soonest first; the others newest first, as the service returns them.
        Items = Show == null ? all : all.Where(a => StateOf(a, now) == Show).ToList();
        if (Show == "upcoming") { Items = Items.OrderBy(a => a.StartUtc).ToList(); }
        Dishes = (await _service.GetDishChoicesAsync())
            .Select(m => new SelectListItem(m.Code == null ? m.Name : $"{m.Code} · {m.Name}", m.Id.ToString())).ToList();
    }
}
