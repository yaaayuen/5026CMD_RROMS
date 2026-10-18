using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
/// <summary>Recorded Paid amounts and counts grouped by payment method.</summary>
public class SalesMethodReportRow
{
    public PaymentMethod Method { get; set; }
    public int Count { get; set; }
    public decimal Amount { get; set; }
}
