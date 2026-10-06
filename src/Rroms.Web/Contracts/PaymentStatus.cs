namespace Rroms.Web.Contracts;
/// <summary>
/// This class contains the payment status information for the system.
/// </summary>

public enum PaymentStatus
{
    Unpaid, //Payment has not been made yet
    Paid, //Payment has been made successfully
    Voided //Payment has been voided or cancelled by staff (e.g., mistake)
}
