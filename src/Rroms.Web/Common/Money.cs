using System.Globalization;
using Rroms.Web.Contracts;
namespace Rroms.Web.Common;
public static class Money
{
    public static string Format(decimal value) => "RM " + value.ToString("N2", CultureInfo.InvariantCulture);
    public static string Method(PaymentMethod method) => method switch {
        PaymentMethod.Cash => "Cash", PaymentMethod.CardTerminal => "Card terminal",
        PaymentMethod.EWallet => "E-wallet", _ => "Archived test payment" };
}
