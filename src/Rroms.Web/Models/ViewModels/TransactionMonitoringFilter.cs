using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
/// <summary>DA-2 validated date range and optional transaction filters.</summary>
public class TransactionMonitoringFilter : MonitoringDateFilter
{
    public PaymentStatus? Status { get; set; }
    public PaymentMethod? Method { get; set; }
}
