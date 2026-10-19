using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;
/// <summary>Exports the same validated report data as DA-3.</summary>
public interface IReportExportService
{
    /// <summary>Builds an administrator download for a selected report and format.</summary>
    Task<ServiceResult<ReportExportFile>> ExportAsync(ReportFilter filter, ReportExportKind kind, ReportExportFormat format);
}
