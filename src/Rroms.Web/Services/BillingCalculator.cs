using Microsoft.Extensions.Options;
using Rroms.Web.Options;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;
public class BillingCalculator(IOptions<BillingOptions> options)
{
    public BillBreakdown Calculate(decimal subtotal)
    {
        var o = options.Value;
        if(subtotal < 0 || o.ServiceChargePercent < 0 || o.ServiceChargePercent > 100 || o.ServiceTaxPercent < 0 || o.ServiceTaxPercent > 100 || !o.HasValidPrecision)
            throw new ArgumentOutOfRangeException(nameof(subtotal));
        decimal Round(decimal x) => decimal.Round(x, 2, MidpointRounding.AwayFromZero);
        subtotal = Round(subtotal);
        var charge = Round(subtotal * o.ServiceChargePercent / 100m);
        var tax = Round((subtotal + (o.TaxIncludesServiceCharge ? charge : 0m)) * o.ServiceTaxPercent / 100m);
        return new() { Subtotal=subtotal, ServiceChargePercent=o.ServiceChargePercent, ServiceCharge=charge,
            ServiceTaxPercent=o.ServiceTaxPercent, ServiceTax=tax, TaxIncludesServiceCharge=o.TaxIncludesServiceCharge, Total=subtotal+charge+tax };
    }
    public static BillBreakdown FromOrder(Order x) => new() { Subtotal=x.Subtotal ?? x.TotalAmount, IsLegacy=x.Subtotal == null,
        ServiceChargePercent=x.ServiceChargePercent, ServiceCharge=x.ServiceCharge, ServiceTaxPercent=x.ServiceTaxPercent,
        ServiceTax=x.ServiceTax, TaxIncludesServiceCharge=x.TaxIncludesServiceCharge, Total=x.TotalAmount };
    public static BillBreakdown FromPayment(Payment x) => new() { Subtotal=x.Subtotal ?? x.Amount, IsLegacy=x.Subtotal == null,
        ServiceChargePercent=x.ServiceChargePercent, ServiceCharge=x.ServiceCharge, ServiceTaxPercent=x.ServiceTaxPercent,
        ServiceTax=x.ServiceTax, TaxIncludesServiceCharge=x.TaxIncludesServiceCharge, Total=x.Amount };
}
