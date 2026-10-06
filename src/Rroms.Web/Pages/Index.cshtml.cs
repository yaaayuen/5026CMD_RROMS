using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Rroms.Web.Pages;

/// <summary>
/// Public home page. Anyone can view it, signed in or not.
/// </summary>
/// <remarks>
/// Static for now. Later sprints add live content here: opening hours (WL, Sprint 2),
/// popular dishes (YJ, Sprint 3) and active announcements (Alex, Sprint 4).
/// </remarks>
public class IndexModel : PageModel
{
    /// <summary>Handles GET /. Nothing to load yet.</summary>
    public void OnGet()
    {
    }
}