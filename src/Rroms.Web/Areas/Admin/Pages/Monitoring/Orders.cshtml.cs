using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Admin.Pages.Monitoring;
/// <summary>DA-2 orders; the existing Admin area policy restricts access to administrators.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class OrdersModel : PageModel
{
    private readonly IMonitoringService _monitoring;
    /// <summary>Receives read-only monitoring queries.</summary>
    public OrdersModel(IMonitoringService monitoring)
    {
        _monitoring = monitoring;
    }
    [BindProperty(SupportsGet = true)]
    public OrderMonitoringFilter Filter { get; set; } = new OrderMonitoringFilter();
    public OrderMonitoringResult Results { get; private set; } = new OrderMonitoringResult();
    public bool Loaded { get; private set; }
    
    /// <summary>Checks binding errors and service validation before showing records or totals.</summary>
    public async Task OnGetAsync()
    {
        
        if (!ModelState.IsValid) { return; }
        var result = await _monitoring.GetOrderMonitoringAsync(Filter);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors) { ModelState.AddModelError(string.Empty, error); }
            return;
        }
        Results = result.Value!;
        Loaded = true;
    }
}
