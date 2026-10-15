using Rroms.Web.Contracts;
using Rroms.Web.Services;

namespace Rroms.Tests.Billing;

/// <summary>Records payment confirmations without delivering messages to real people.</summary>
public class BillingNotificationRecorder : INotificationService
{
    public int Count { get; private set; }
    public string? UserId { get; private set; }
    public NotificationType Type { get; private set; }
    public string? Message { get; private set; }
    public string? Link { get; private set; }
    public bool ThrowOnNotify { get; set; }
    public Func<Task>? BeforeNotify { get; set; }
    /// <summary>Captures the confirmation and optionally simulates an unavailable notification service.</summary>
    public async Task NotifyUserAsync(string userId, NotificationType type, string title, string message, string? linkUrl = null)
    {
        if (BeforeNotify != null)
        {
            await BeforeNotify();
        }
        if (ThrowOnNotify)
        {
            throw new InvalidOperationException("Test notification failure");
        }
        Count++;
        UserId = userId;
        Type = type;
        Message = message;
        Link = linkUrl;
    }
    /// <summary>DA-1 publishes to the owning customer, not a role.</summary>
    public Task NotifyRoleAsync(string roleName, NotificationType type, string title, string message, string? linkUrl = null)
    {
        throw new InvalidOperationException("Billing should notify the customer only.");
    }
}
