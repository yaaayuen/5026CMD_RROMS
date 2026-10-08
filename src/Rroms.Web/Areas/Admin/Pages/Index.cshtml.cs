using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Areas.Admin.Pages;

/// <summary>
/// Administrator home page: entry point to every admin module.
/// </summary>
/// <remarks>
/// Each tile shows which sprint builds the module. When a module is finished, its owner passes
/// the page URL as the last constructor argument so the tile becomes a link.
/// Access to this area is restricted to the correct role in YY-3.
/// </remarks>
public class IndexModel : PageModel
{
    /// <summary>The module tiles shown on this dashboard.</summary>
    public IReadOnlyList<ModuleTileViewModel> Tiles { get; private set; } = new List<ModuleTileViewModel>();

    /// <summary>Handles GET /Admin.</summary>
    public void OnGet()
    {
        Tiles = new List<ModuleTileViewModel>
        {
            new ModuleTileViewModel(
                "Restaurant setup",
                "Dining areas, tables, opening hours and menu categories.",
                "settings",
                "Sprint 2 · WL"),
            new ModuleTileViewModel(
                "Menu items",
                "Dishes, prices, availability, offers and photos.",
                "menu",
                "Sprint 3 · YJ"),
            new ModuleTileViewModel(
                "Accounts and roles",
                "Create staff accounts, assign roles and deactivate users.",
                "users",
                "Sprint 1 · YY",
                "/Admin/Accounts"),
            new ModuleTileViewModel(
                "Announcements",
                "Promotional campaigns and restaurant notices.",
                "megaphone",
                "Sprint 4 · Alex"),
            new ModuleTileViewModel(
                "Analytics dashboard",
                "Sales, reservations, table occupancy and popular dishes.",
                "chart",
                "Sprint 2 · WL"),
            new ModuleTileViewModel(
                "Reports and exports",
                "Filtered reports with CSV, Excel and PDF export.",
                "receipt",
                "Sprint 5 · Darren"),
            new ModuleTileViewModel(
                "Monitoring",
                "Live view of reservations, orders and transactions.",
                "calendar",
                "Sprint 5 · Darren"),
            new ModuleTileViewModel(
                "Customer feedback",
                "Ratings and comments from completed visits.",
                "star",
                "Sprint 4 · Alex")
        };
    }
}