using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Table occupancy and reservation analytics for the administrator (WL-4).
/// </summary>
public interface IAnalyticsService
{
    /// <summary>
    /// Builds the occupancy report for a range of Malaysia dates (both days included):
    /// table utilisation %, peak hours, the daily reservation trend and upcoming confirmed bookings.
    /// </summary>
    /// <param name="from">First day of the range.</param>
    /// <param name="to">Last day of the range.</param>
    /// <returns>The report, or an error when the range is invalid (end before start, or too long).</returns>
    Task<ServiceResult<OccupancyReport>> GetOccupancyReportAsync(DateOnly from, DateOnly to);
}
