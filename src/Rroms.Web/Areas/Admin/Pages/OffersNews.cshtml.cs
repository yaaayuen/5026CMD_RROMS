using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Areas.Admin.Pages;

/// <summary>
/// Offers and news for administrators: promotions and announcements, the two things that decide which deals
/// and notices customers see, grouped so the Admin dashboard needs only one tile for them.
/// </summary>
public class OffersNewsModel : PageModel
{
    /// <summary>The two tiles.</summary>
    public IReadOnlyList<ModuleTileViewModel> Tiles { get; private set; } = new List<ModuleTileViewModel>();

    /// <summary>Handles GET /Admin/OffersNews.</summary>
    public void OnGet()
    {
        Tiles = new List<ModuleTileViewModel>
        {
            new ModuleTileViewModel(
                "Announcements",
                "Promotional campaigns and restaurant notices.",
                "megaphone",
                "Sprint 4 · Alex",
                "/Admin/Announcements"),
            new ModuleTileViewModel(
                "Promotions",
                "Percentage discounts by date, day and time.",
                "megaphone",
                "Sprint 3 · YJ",
                "/Admin/Discounts")
        };
    }
}
