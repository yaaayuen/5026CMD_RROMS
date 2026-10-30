using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Areas.Admin.Pages;

/// <summary>
/// Insights and reports for administrators: the read-only pages that show figures, charts and records,
/// grouped so the Admin dashboard needs only one tile for them.
/// </summary>
public class InsightsModel : PageModel
{
    /// <summary>The five insight tiles.</summary>
    public IReadOnlyList<ModuleTileViewModel> Tiles { get; private set; } = new List<ModuleTileViewModel>();

    /// <summary>Handles GET /Admin/Insights.</summary>
    public void OnGet()
    {
        Tiles = new List<ModuleTileViewModel>
        {
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
                "Kitchen performance",
                "Orders, waiting and cooking times, and the busiest hour.",
                "chart",
                "Sprint 4 · Alex",
                "/Admin/Kitchen"),
            new ModuleTileViewModel(
                "Chatbot insights",
                "Most asked topics and how often the AI answered.",
                "chat",
                "Sprint 4 · Alex",
                "/Admin/ChatInsights")
        };
    }
}
