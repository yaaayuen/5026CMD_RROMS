namespace Rroms.Web.Models.ViewModels;
public class BillBreakdown
{
    public decimal Subtotal { get; set; }
    public decimal ServiceChargePercent { get; set; }
    public decimal ServiceCharge { get; set; }
    public decimal ServiceTaxPercent { get; set; }
    public decimal ServiceTax { get; set; }
    public bool TaxIncludesServiceCharge { get; set; }
    public decimal Total { get; set; }
    public bool IsLegacy { get; set; }
}
