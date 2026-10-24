using Rroms.Web.Contracts;
namespace Rroms.Web.Common;
public static class PaymentMethods
{
    public static IReadOnlyList<PaymentMethod> Supported { get; } = Array.AsReadOnly(new[] { PaymentMethod.Cash, PaymentMethod.CardTerminal, PaymentMethod.EWallet });
    public static bool IsSupported(PaymentMethod method) => Supported.Contains(method);
}
