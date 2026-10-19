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
    private readonly IReportExportService _exports;
    /// <summary>Receives the reporting service.</summary>
    public IndexModel(IReportService reports, IReportExportService exports)
    {
        _reports = reports;
        _exports = exports;
    }
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
    /// <summary>Downloads a validated report; the Admin area policy also protects this handler.</summary>
    public async Task<IActionResult> OnGetExportAsync(ReportExportKind kind, ReportExportFormat format)
    {
        if (!ModelState.IsValid) { return Page(); }
        var result = await _exports.ExportAsync(Filter, kind, format);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors) { ModelState.AddModelError(string.Empty, error); }
            return Page();
        }
        var download = result.Value!;
        return File(download.Content, download.ContentType, download.FileName);
    }
}
