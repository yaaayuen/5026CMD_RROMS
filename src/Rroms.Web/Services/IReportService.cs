using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;
/// <summary>Darren's administrator reports. Export formatting belongs to DA-4.</summary>
public interface IReportService
{
    /// <summary>Builds reservation, Paid sales, popular-item and customer-activity reports for a validated period.</summary>
    Task<ServiceResult<ReportDashboard>> GetReportAsync(ReportFilter filter);
}
