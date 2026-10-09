using Rroms.Web.Models.ViewModels;

namespace Rroms.Web.Services;

/// <summary>
/// Reading side of notifications: what the signed-in user sees in their inbox (YY-6).
/// Every method takes the user ID from the cookie and filters by it,
/// so one user can never read or change another user's notifications.
/// </summary>

public interface INotificationInbox
{
    //The user's most recent notifications, newest first
    Task<IReadOnlyList<NotificationItem>> GetRecentAsync(string userId, int count);

    //How many of the user's notifications are unread (for the bell badge)
    Task<int> GetUnreadCountAsync(string userId);

    //Marks one notification as read. Returns it, or null if it does not exist
    //or belongs to someone else (both cases look the same to the caller, on purpose).
    Task<NotificationItem?> MarkAsReadAsync(string userId, int notificationId);

    //Marks all the user's notifications as read and returns how many changed
    Task<int> MarkAllAsReadAsync(string userId);
}
