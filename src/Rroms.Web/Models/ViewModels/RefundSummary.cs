using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
public class RefundSummary
{
    public int PaymentId { get; set; }
    public Guid BatchId { get; set; }
    public string CustomerName { get; set; } = "";
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public RefundStatus Status { get; set; }
    public string Reason { get; set; } = "";
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? ProcessedById { get; set; }
    public string? DecisionReason { get; set; }
}
public class RefundList
{
    public List<RefundSummary> Rows { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
}
