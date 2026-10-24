using System.ComponentModel.DataAnnotations;
namespace Rroms.Web.Options;
public class BillingOptions
{
    [Range(0,100)] public decimal ServiceChargePercent { get; set; } = 10m;
    [Range(0,100)] public decimal ServiceTaxPercent { get; set; } = 6m;
    public bool TaxIncludesServiceCharge { get; set; } = true;
    public bool HasValidPrecision => decimal.Round(ServiceChargePercent,2)==ServiceChargePercent && decimal.Round(ServiceTaxPercent,2)==ServiceTaxPercent;
}
