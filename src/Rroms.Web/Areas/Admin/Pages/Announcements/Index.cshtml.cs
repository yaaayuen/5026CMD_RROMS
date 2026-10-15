using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Admin.Pages.Announcements;

/// <summary>Creates, edits, schedules and unpublishes announcements.</summary>
public class IndexModel : PageModel
{
    private readonly IAnnouncementService _service;
    public IndexModel(IAnnouncementService service) { _service = service; }
    [BindProperty] public Announcement Input { get; set; } = new Announcement();
    [BindProperty] public DateTime StartLocal { get; set; } = MalaysiaTime.FromUtc(DateTime.UtcNow);
    [BindProperty] public DateTime EndLocal { get; set; } = MalaysiaTime.FromUtc(DateTime.UtcNow).AddDays(7);
    public List<Announcement> Items { get; set; } = new List<Announcement>();
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Items = await _service.GetAsync(false);
        if (id.HasValue)
        {
            var item = Items.SingleOrDefault(a => a.Id == id.Value);
            if (item == null) { return NotFound(); }
            Input = item;
            StartLocal = MalaysiaTime.FromUtc(item.StartUtc);
            EndLocal = MalaysiaTime.FromUtc(item.EndUtc);
        }
        return Page();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid)
        {
            Input.StartUtc = MalaysiaTime.ToUtc(StartLocal);
            Input.EndUtc = MalaysiaTime.ToUtc(EndLocal);
            var result = await _service.SaveAsync(Input);
            if (result.Succeeded) { TempData["StatusMessage"] = "Announcement saved."; return RedirectToPage(); }
            foreach (string error in result.Errors) { ModelState.AddModelError("", error); }
        }
        Items = await _service.GetAsync(false);
        return Page();
    }
}
