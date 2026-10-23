using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models;
using Rroms.Web.Services;
namespace Rroms.Web.Pages.Announcements;

/// <summary>Public announcements whose publication dates are active, filtered by the All, Offers or News tab.</summary>
public class IndexModel : PageModel
{
    private readonly IAnnouncementService _service;
    public IndexModel(IAnnouncementService service) { _service = service; }
    public List<Announcement> Items { get; set; } = new List<Announcement>();
    /// <summary>The tab from the query string: "offers", "news", or anything else for all.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Show { get; set; }
    public int AllCount { get; set; }
    public int OfferCount { get; set; }
    public int NewsCount => AllCount - OfferCount;
    public async Task OnGetAsync()
    {
        List<Announcement> active = await _service.GetAsync(true);
        AllCount = active.Count;
        OfferCount = active.Count(a => a.IsPromotion);
        Show = Show is "offers" or "news" ? Show : null;
        Items = Show switch
        {
            "offers" => active.Where(a => a.IsPromotion).ToList(),
            "news" => active.Where(a => !a.IsPromotion).ToList(),
            _ => active
        };
    }
}
