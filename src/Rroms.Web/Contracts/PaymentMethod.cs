namespace Rroms.Web.Contracts;
/// <summary>
/// This class contains the payment method information for the system.
/// </summary>

public enum PaymentMethod
{
    Cash, //Payment is made in cash at the counter
    CardTerminal, //Payment is made using a card terminal
    EWallet, // TnG, DuitNow, GrabPay, etc.
    SimulatedOnline //"Pay Now" button for customers to simulate online payment (for testing purposes)
}
