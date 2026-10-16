namespace Rroms.Web.Models.ViewModels;

/// <summary>One bill line using the unit price saved when the order was placed.</summary>
public class BillLine
{
    public string DishName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => Quantity * UnitPrice;
}
