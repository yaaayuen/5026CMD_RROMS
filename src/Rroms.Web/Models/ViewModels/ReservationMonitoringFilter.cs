using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
/// <summary>DA-2 validated date range and optional reservation filters.</summary>
public class ReservationMonitoringFilter : MonitoringDateFilter
{
    public ReservationStatus? Status { get; set; }
    public int? AreaId { get; set; }
}
