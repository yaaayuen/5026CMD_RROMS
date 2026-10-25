namespace Rroms.Web.Models.ViewModels;

/// <summary>A dish line staff chose to refund. The amount is always calculated on the server.</summary>
public class RefundLineInput
{
    public int OrderItemId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>A refunded dish shown on the receipt.</summary>
public class RefundLineSummary
{
    public string DishName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
}
