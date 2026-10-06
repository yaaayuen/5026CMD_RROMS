using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Areas.Staff.Pages;

/// <summary>
/// Staff home page: entry point to daily operations.
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

    /// <summary>Handles GET /Staff.</summary>
    public void OnGet()
    {
        Tiles = new List<ModuleTileViewModel>
        {
            new ModuleTileViewModel(
                "Today's reservations",
                "Confirm, change or cancel bookings and see special requests.",
                "calendar",
                "Sprint 5 · Darren"),
            new ModuleTileViewModel(
                "Order queue",
                "Move orders from Preparing to Ready to Serve to Served.",
                "flame",
                "Sprint 4 · Alex"),
            new ModuleTileViewModel(
                "Billing and payments",
                "Generate bills and record cash, card or e-wallet payments.",
                "receipt",
                "Sprint 5 · Darren"),
            new ModuleTileViewModel(
                "My profile",
                "Update your contact details and password.",
                "users",
                "Sprint 1 · YY")
        };
    }
}