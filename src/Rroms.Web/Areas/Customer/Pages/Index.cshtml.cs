using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Areas.Customer.Pages;

/// <summary>
/// Customer home page: entry point to bookings, orders and history.
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

    /// <summary>Handles GET /Customer.</summary>
    public void OnGet()
    {
        Tiles = new List<ModuleTileViewModel>
        {
            new ModuleTileViewModel(
                "Book a table",
                "Choose a time and party size; we show only free tables.",
                "calendar",
                "Sprint 2 · WL",
                "/Customer/Reservations/Book"),
            new ModuleTileViewModel(
                "Menu and ordering",
                "Browse the menu and order to your table.",
                "menu",
                "Sprint 3 · YJ",
                "/Menu"),
            new ModuleTileViewModel(
                "Live order tracking",
                "Watch your order go from the grill to your table.",
                "flame",
                "Sprint 4 · Alex",
                "/Customer/Tracking"),
            new ModuleTileViewModel(
                "My history",
                "Past reservations, orders and payment receipts.",
                "receipt",
                "Sprint 3 · YJ",
                "/Customer/Reservations"),
            new ModuleTileViewModel(
                "Notifications",
                "Booking confirmations, order updates and offers.",
                "bell",
                "Sprint 1 · YY",
                "/Notifications"),
            new ModuleTileViewModel(
                "My profile",
                "Contact details and dining preferences.",
                "users",
                "Sprint 1 · YY",
                "/Account/Profile"),
            new ModuleTileViewModel(
                "Ask our assistant",
                "Questions about the menu, hours or booking.",
                "chat",
                "Sprint 4 · Alex",
                "/Chat")
        };
    }
}