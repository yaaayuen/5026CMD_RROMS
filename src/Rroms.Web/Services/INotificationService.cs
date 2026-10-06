using Rroms.Web.Contracts;

namespace Rroms.Web.Services;

/// <summary>
/// The one channel every module uses to tell people that something happened.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Notifies one user, for example the customer whose reservation was confirmed.
    /// </summary>
    /// <param name="userId">Identity user ID of the recipient.</param>
    /// <param name="type">Category, used for the icon and email subject.</param>
    /// <param name="title">Short headline, e.g. "Reservation confirmed".</param>
    /// <param name="message">Full sentence shown in the inbox and email body.</param>
    /// <param name="linkUrl">Optional relative link to the related page, e.g. "/Customer/Reservations/Details/5".</param>
    Task NotifyUserAsync(string userId, NotificationType type, string title, string message, string? linkUrl = null);

    /// <summary>
    /// Notifies everyone in a role, for example all staff when a new booking arrives.
    /// </summary>
    /// <param name="roleName">One of the values in <see cref="Constants.RoleNames"/>.</param>
    /// <param name="type">Category of the notification.</param>
    /// <param name="title">Short headline.</param>
    /// <param name="message">Full sentence shown to each recipient.</param>
    /// <param name="linkUrl">Optional relative link to the related page.</param>
    Task NotifyRoleAsync(string roleName, NotificationType type, string title, string message, string? linkUrl = null);
}
