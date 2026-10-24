using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Web.Pages.Announcements;

/// <summary>Public announcements whose publication dates are active, filtered by the All, Offers or News tab, and recent guest reviews.</summary>
public class IndexModel : PageModel
{
    private readonly IAnnouncementService _service;
    private readonly IFeedbackService _feedback;
    public IndexModel(IAnnouncementService service, IFeedbackService feedback)
    {
        _service = service;
        _feedback = feedback;
    }
    public List<Announcement> Items { get; set; } = new List<Announcement>();
    /// <summary>The tab from the query string: "offers", "news", or anything else for all.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Show { get; set; }
    public int AllCount { get; set; }
    public int OfferCount { get; set; }
    public int NewsCount => AllCount - OfferCount;
    /// <summary>Reviews an administrator has not hidden. Hidden or deleted comments never reach this list.</summary>
    public List<Feedback> Reviews { get; set; } = new List<Feedback>();
    public FeedbackSummary Ratings { get; set; } = new FeedbackSummary();
    /// <summary>First name only, so a review never shows a guest's full name.</summary>
    public static string GuestName(Feedback review)
    {
        string name = review.Reservation?.Customer?.FullName.Trim() ?? "";
        return name.Length == 0 ? "A guest" : name.Split(' ')[0];
    }
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
        Reviews = await _feedback.GetPublicAsync(6);
        Ratings = await _feedback.GetSummaryAsync(0);
    }
}
