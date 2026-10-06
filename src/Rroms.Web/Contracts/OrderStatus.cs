namespace Rroms.Web.Contracts;
/// <summary>
/// This class contains the order status information for the system.
/// </summary>

public enum OrderStatus
{
    Placed, //Order has been placed by the customer, waiting for staff to prepare
    Preparing, //Order is being prepared by the staff
    ReadyToServe, //Food is ready to be served to the customer
    Served, //Food has been served to the customer
    Completed, //Order is completed and can be billed
    Cancelled //Order has been cancelled by the customer or staff before completion
}
