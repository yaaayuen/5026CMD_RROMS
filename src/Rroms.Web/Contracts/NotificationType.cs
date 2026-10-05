namespace Rroms.Web.Contracts;
/// <summary>
/// This class contains the notification types information for the system.
/// </summary>

public enum NotificationType
{
    General, //General notification for all users
    ReservationConfirmed, //Notification for reservation confirmed
    ReservationUpdated, //Notification for reservation updated
    ReservationCancelled, //Notification for reservation cancelled
    OrderUpdated, //Notification for order updated
    PaymentConfirmed, //Notification for payment confirmed
    Promotion //Notification for promotion or special offer
}
