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
public class IndexModel(Rroms.Web.Services.IBillingService billing) : PageModel
{
    public int UnpaidBills { get; private set; }
    /// <summary>The module tiles shown on this dashboard.</summary>
    public IReadOnlyList<ModuleTileViewModel> Tiles { get; private set; } = new List<ModuleTileViewModel>();

    /// <summary>Handles GET /Staff.</summary>
    public async Task OnGetAsync()
    {
        UnpaidBills = (await billing.GetDashboardAsync()).UnpaidCount;
        List<ModuleTileViewModel> tiles = DeskTiles();
        tiles.Add(new ModuleTileViewModel(
            "My profile",
            "Update your contact details and password.",
            "users",
            "Sprint 1 · YY",
            "/Account/Profile"));
        Tiles = tiles;
    }

    /// <summary>
    /// The four daily-operations tiles. Also shown to administrators on Admin/StaffDesk, so both dashboards
    /// always offer the same pages.
    /// </summary>
    public static List<ModuleTileViewModel> DeskTiles()
    {
        return new List<ModuleTileViewModel>
        {
            new ModuleTileViewModel(
                "Today's reservations",
                "Confirm, change or cancel bookings and see special requests.",
                "calendar",
                "Sprint 2 · WL",
                "/Staff/Reservations"),
            // DA-7 (Darren): separate from WL's daily status-actions page.
            new ModuleTileViewModel(
                "Reservation history and filters",
                "Filter bookings, read dining preferences and view customer history.",
                "calendar",
                "Sprint 5 · Darren",
                "/Staff/Reservations/History"),
            new ModuleTileViewModel(
                "Kitchen orders",
                "Move orders from Preparing to Ready to Serve to Served.",
                "flame",
                "Sprint 4 · Alex",
                "/Staff/Orders"),
            new ModuleTileViewModel(
                "Billing and payments",
                "Generate bills and record cash, card or e-wallet payments.",
                "receipt",
                "Sprint 5 · Darren",
                "/Staff/Billing")
        };
    }
}