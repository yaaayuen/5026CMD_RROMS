using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Admin.Pages.Monitoring;
/// <summary>DA-2 transactions; the existing Admin area policy restricts access to administrators.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TransactionsModel : PageModel
{
    private readonly IMonitoringService _monitoring;
    /// <summary>Receives read-only monitoring queries.</summary>
    public TransactionsModel(IMonitoringService monitoring)
    {
        _monitoring = monitoring;
    }
    [BindProperty(SupportsGet = true)]
    public TransactionMonitoringFilter Filter { get; set; } = new TransactionMonitoringFilter();
    public TransactionMonitoringResult Results { get; private set; } = new TransactionMonitoringResult();
    public bool Loaded { get; private set; }
    
    /// <summary>Checks binding errors and service validation before showing records or totals.</summary>
    public async Task OnGetAsync()
    {
        
        if (!ModelState.IsValid) { return; }
        var result = await _monitoring.GetTransactionMonitoringAsync(Filter);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors) { ModelState.AddModelError(string.Empty, error); }
            return;
        }
        Results = result.Value!;
        Loaded = true;
    }
}
