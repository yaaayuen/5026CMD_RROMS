using Rroms.Web.Contracts;
namespace Rroms.Web.Models.ViewModels;
/// <summary>An item ranked by units in orders paid within the range; prices are saved order-line prices.</summary>
public class PopularItemReportRow
{
    public int Rank { get; set; }
    public int MenuItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int DemoQuantity { get; set; }
    public decimal LineValue { get; set; }
    public decimal DemoLineValue { get; set; }
}
