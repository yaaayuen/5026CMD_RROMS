using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Admin.Pages.Reports;
/// <summary>Administrator-only DA-3 reports, protected by the existing area policy.</summary>
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel : PageModel
{
    private readonly IReportService _reports;
    /// <summary>Receives the reporting service.</summary>
    public IndexModel(IReportService reports) { _reports = reports; }
    [BindProperty(SupportsGet = true)]
    public ReportFilter Filter { get; set; } = new ReportFilter();
    public ReportDashboard Results { get; private set; } = new ReportDashboard();
    public bool Loaded { get; private set; }
    /// <summary>Loads a validated report without changing business records.</summary>
    public async Task OnGetAsync()
    {
        if (!ModelState.IsValid) { return; }
        var result = await _reports.GetReportAsync(Filter);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors) { ModelState.AddModelError(string.Empty, error); }
            return;
        }
        Results = result.Value!;
        Loaded = true;
    }
}
