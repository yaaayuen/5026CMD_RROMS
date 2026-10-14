using Rroms.Web.Contracts;
using Rroms.Web.Services;

namespace Rroms.Tests.Menu;

/// <summary>
/// A stand-in for the real notification channel: it sends nothing, it only remembers what it was asked to send,
/// so a test can check who was told what. Owner: YJ.
/// </summary>
public class FakeNotificationService : INotificationService
{
    /// <summary>Everything sent to one user.</summary>
    public List<RecordedMessage> ToUsers { get; } = new List<RecordedMessage>();

    /// <summary>Everything sent to a whole role (for example all staff).</summary>
    public List<RecordedMessage> ToRoles { get; } = new List<RecordedMessage>();

    /// <inheritdoc />
    public Task NotifyUserAsync(string userId, NotificationType type, string title, string message, string? linkUrl = null)
    {
        ToUsers.Add(new RecordedMessage(userId, type, title, message, linkUrl));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NotifyRoleAsync(string roleName, NotificationType type, string title, string message, string? linkUrl = null)
    {
        ToRoles.Add(new RecordedMessage(roleName, type, title, message, linkUrl));

        return Task.CompletedTask;
    }

    /// <summary>One remembered notification.</summary>
    /// <param name="Recipient">The user ID, or the role name.</param>
    /// <param name="Type">The notification category.</param>
    /// <param name="Title">The headline.</param>
    /// <param name="Message">The full sentence.</param>
    /// <param name="LinkUrl">The related page, if any.</param>
    public record RecordedMessage(string Recipient, NotificationType Type, string Title, string Message, string? LinkUrl);
}
