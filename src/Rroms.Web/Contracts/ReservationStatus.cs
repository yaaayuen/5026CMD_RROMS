namespace Rroms.Web.Contracts;
/// <summary>
/// This class contains the reservation status information for the system.
/// </summary>

public enum ReservationStatus
{
    Pending, //Requested by customer, waiting for staff to confirm
    Confirmed, //Accepted by staff, table reserved
    Seated, //Customer has arrived and is seated at the table
    Completed, //The visit is completed, and the customer has left the restaurant (can get a review)
    Cancelled, //Cancelled by customer or staff, table released
    NoShow //Customer did not show up for the reservation, table released
}
