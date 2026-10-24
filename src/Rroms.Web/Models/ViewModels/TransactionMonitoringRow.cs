using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
/// <summary>Read-only transaction row for administrator monitoring.</summary>
public class TransactionMonitoringRow
{
    public RefundStatus? RefundStatus { get; set; }
    public string? VoidReason { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public string? VoidedById { get; set; }
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string StaffName { get; set; } = string.Empty;
    public DateTime PaidAtUtc { get; set; }
    public PaymentStatus Status { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
}
