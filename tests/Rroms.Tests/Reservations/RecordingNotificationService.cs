using Rroms.Web.Contracts;
using Rroms.Web.Services;

namespace Rroms.Tests.Reservations;

/// <summary>
/// A stand-in for <see cref="INotificationService"/> in unit tests: instead of saving, pushing and
/// emailing, it only records who would have been notified, so tests can check that a notification was sent.
/// </summary>
public class RecordingNotificationService : INotificationService
{
    /// <summary>User IDs passed to NotifyUserAsync, in order.</summary>
    public List<string> NotifiedUsers { get; } = new List<string>();

    /// <summary>Role names passed to NotifyRoleAsync, in order.</summary>
    public List<string> NotifiedRoles { get; } = new List<string>();

    /// <inheritdoc />
    public Task NotifyUserAsync(string userId, NotificationType type, string title, string message, string? linkUrl = null)
    {
        NotifiedUsers.Add(userId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifyRoleAsync(string roleName, NotificationType type, string title, string message, string? linkUrl = null)
    {
        NotifiedRoles.Add(roleName);
        return Task.CompletedTask;
    }
}
