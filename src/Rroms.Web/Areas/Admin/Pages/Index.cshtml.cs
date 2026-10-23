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
                "Sprint 2 · WL",
                "/Admin/Restaurant"),
            new ModuleTileViewModel(
                "Menu items",
                "Dishes, prices, availability, offers and photos.",
                "menu",
                "Sprint 3 · YJ",
                "/Admin/Menu"),
            new ModuleTileViewModel(
                "Accounts and roles",
                "Create staff accounts, assign roles and deactivate users.",
                "users",
                "Sprint 1 · YY",
                "/Admin/Accounts"),
            new ModuleTileViewModel(
                "Dietary tags",
                "Add the food tags customers can avoid and dishes can be marked with.",
                "flame",
                "Sprint 1 · YY",
                "/Admin/DietaryTags"),
            new ModuleTileViewModel(
                "Announcements",
                "Promotional campaigns and restaurant notices.",
                "megaphone",
                "Sprint 4 · Alex",
                "/Admin/Announcements"),
            new ModuleTileViewModel(
                "Kitchen orders",
                "Move orders from Preparing to Ready to Serve to Served.",
                "flame",
                "Sprint 4 · Alex",
                "/Staff/Orders"),
            new ModuleTileViewModel(
                "Analytics dashboard",
                "Sales, reservations, table occupancy and popular dishes.",
                "chart",
                "Sprint 2 · WL",
                "/Admin/Analytics"),
            new ModuleTileViewModel(
                "Reports",
                "Reservation, sales, popular-item and customer-activity reports.",
                "receipt",
                "Sprint 5 · Darren",
                "/Admin/Reports"),
            new ModuleTileViewModel(
                "Monitoring",
                "Filter reservations, orders and payment records.",
                "calendar",
                "Sprint 5 · Darren",
                "/Admin/Monitoring/Reservations"),
            new ModuleTileViewModel(
                "Customer feedback",
                "Ratings and comments from completed visits.",
                "star",
                "Sprint 4 · Alex",
                "/Admin/Feedback")
        };
    }
}