using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
/// <summary>DA-2 validated date range and optional order filters.</summary>
public class OrderMonitoringFilter : MonitoringDateFilter
{
    public OrderStatus? Status { get; set; }
}
