using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Web.Areas.Admin.Pages;

/// <summary>
/// Staff desk for administrators: the same daily-operations tiles as the Staff dashboard (without My profile,
/// which the administrator reaches from the navbar), so the Admin dashboard only needs one tile for them.
/// </summary>
public class StaffDeskModel(IBillingService billing) : PageModel
{
    /// <summary>Unpaid bills, shown on the Billing tile like on the Staff dashboard.</summary>
    public int UnpaidBills { get; private set; }

    /// <summary>The four staff tiles.</summary>
    public IReadOnlyList<ModuleTileViewModel> Tiles { get; private set; } = new List<ModuleTileViewModel>();

    /// <summary>Handles GET /Admin/StaffDesk.</summary>
    public async Task OnGetAsync()
    {
        UnpaidBills = (await billing.GetDashboardAsync()).UnpaidCount;
        Tiles = Rroms.Web.Areas.Staff.Pages.IndexModel.DeskTiles();
    }
}
