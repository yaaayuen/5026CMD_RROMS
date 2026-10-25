using System.ComponentModel.DataAnnotations;
using Rroms.Web.Contracts;
namespace Rroms.Web.Models;
/// <summary>One refund audit row per original payment; a receipt can group several rows. Lines list refunded dishes.</summary>
public class PaymentRefund
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public Payment? Payment { get; set; }
    public Guid BatchId { get; set; }
    public decimal Amount { get; set; }
    public RefundStatus Status { get; set; }
    [Required, StringLength(200)] public string Reason { get; set; } = "";
    [Required, StringLength(450)] public string RequestedById { get; set; } = "";
    public DateTime RequestedAtUtc { get; set; }
    [StringLength(450)] public string? ProcessedById { get; set; }
    [StringLength(200)] public string? DecisionReason { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    /// <summary>The refunded dishes; empty for a whole-receipt refund.</summary>
    public List<PaymentRefundLine> Lines { get; set; } = new();
}
