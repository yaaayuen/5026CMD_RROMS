using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
/// <summary>Read-only order row for administrator monitoring.</summary>
public class OrderMonitoringRow
{
    public int Id { get; set; }
    public int ReservationId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime PlacedAtUtc { get; set; }
    public string TableLabel { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
}
