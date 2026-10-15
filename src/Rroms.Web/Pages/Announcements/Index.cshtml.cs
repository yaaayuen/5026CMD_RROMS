using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models;
using Rroms.Web.Services;
namespace Rroms.Web.Pages.Announcements;

/// <summary>Public announcements whose publication dates are active.</summary>
public class IndexModel : PageModel
{
    private readonly IAnnouncementService _service;
    public IndexModel(IAnnouncementService service) { _service = service; }
    public List<Announcement> Items { get; set; } = new List<Announcement>();
    public async Task OnGetAsync() { Items = await _service.GetAsync(true); }
}
