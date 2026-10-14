using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
using Rroms.Web.Services.Email;

namespace Rroms.Web.Services;

/// <summary>
/// The real notification system (YY-6): saves to the inbox, pushes live, and queues an email.
/// Implements both the publishing side (<see cref="INotificationService"/>, used by every module)
/// and the reading side (<see cref="INotificationInbox"/>, used by the inbox page and bell).
/// </summary>
public class NotificationService : INotificationService, INotificationInbox
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationPusher _pusher;
    private readonly IEmailQueue _emailQueue;
    private readonly SmtpOptions _smtpOptions;
    private readonly ILogger<NotificationService> _logger;

    //All dependencies are supplied by dependency injection
    public NotificationService(
        ApplicationDbContext context,
        INotificationPusher pusher,
        IEmailQueue emailQueue,
        IOptions<SmtpOptions> smtpOptions,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _pusher = pusher;
        _emailQueue = emailQueue;
        _smtpOptions = smtpOptions.Value;
        _logger = logger;
    }

    // ---------------------------------------------------------------------
    // Publishing (INotificationService)
    // ---------------------------------------------------------------------

    public async Task NotifyUserAsync(string userId, NotificationType type, string title, string message, string? linkUrl = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A recipient user ID is required.", nameof(userId));
        }

        Recipient? recipient = await _context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new Recipient { UserId = user.Id, Email = user.Email })
            .FirstOrDefaultAsync();

        if (recipient == null)
        {
            _logger.LogWarning("Notification '{Title}' skipped: user {UserId} does not exist.", title, userId);
            return;
        }

        await DeliverAsync(new List<Recipient> { recipient }, type, title, message, linkUrl);
    }

    public async Task NotifyRoleAsync(string roleName, NotificationType type, string title, string message, string? linkUrl = null)
    {
        if (!RoleNames.All.Contains(roleName))
        {
            throw new ArgumentException($"Unknown role '{roleName}'.", nameof(roleName));
        }

        // Every ACTIVE user in the role. Deactivated accounts receive nothing.
        List<Recipient> recipients = await (
            from user in _context.Users.AsNoTracking()
            join userRole in _context.UserRoles on user.Id equals userRole.UserId
            join role in _context.Roles on userRole.RoleId equals role.Id
            where role.Name == roleName && user.IsActive
            select new Recipient { UserId = user.Id, Email = user.Email })
            .ToListAsync();

        if (recipients.Count == 0)
        {
            return;
        }

        await DeliverAsync(recipients, type, title, message, linkUrl);
    }

    //Saves, pushes and emails one notification to each recipient
    private async Task DeliverAsync(
        List<Recipient> recipients, NotificationType type, string title, string message, string? linkUrl)
    {
        // Trim to the column sizes so a long message from another module can never cause a database error.
        string safeTitle = Truncate(title.Trim(), Notification.TitleMaxLength);
        string safeMessage = Truncate(message.Trim(), Notification.MessageMaxLength);
        string? safeLink = linkUrl == null ? null : Truncate(linkUrl, Notification.LinkMaxLength);
        DateTime now = DateTime.UtcNow;

        // 1. Database: one row per recipient, saved in a single round trip.
        foreach (Recipient recipient in recipients)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = recipient.UserId,
                Type = type,
                Title = safeTitle,
                Message = safeMessage,
                LinkUrl = safeLink,
                IsRead = false,
                CreatedAtUtc = now
            });
        }

        await _context.SaveChangesAsync();

        // 2. Live push to any open browser tabs.
        var payload = new NotificationPushPayload
        {
            Title = safeTitle,
            Message = safeMessage,
            LinkUrl = safeLink,
            Type = type.ToString()
        };

        try
        {
            List<string> userIds = recipients.Select(recipient => recipient.UserId).ToList();
            await _pusher.PushAsync(userIds, payload);
        }
        catch (Exception exception)
        {
            // The notification is already saved, so a push failure only delays it until the next page load.
            _logger.LogWarning(exception, "Live push failed for notification '{Title}'.", safeTitle);
        }

        // 3. Email, through the background queue.
        foreach (Recipient recipient in recipients)
        {
            if (!string.IsNullOrWhiteSpace(recipient.Email))
            {
                _emailQueue.TryEnqueue(BuildEmail(recipient.Email, safeTitle, safeMessage, safeLink));
            }
        }
    }

    //Builds a simple branded HTML email. All text is HTML-encoded to prevent injection
    private EmailMessage BuildEmail(string toAddress, string title, string message, string? linkUrl)
    {
        string encodedTitle = WebUtility.HtmlEncode(title);
        string encodedMessage = WebUtility.HtmlEncode(message);

        string linkHtml = string.Empty;
        if (!string.IsNullOrEmpty(linkUrl) && linkUrl.StartsWith('/'))
        {
            // Emails need a full address; only our own relative links are turned into links.
            string fullUrl = _smtpOptions.SiteBaseUrl.TrimEnd('/') + linkUrl;
            linkHtml = $"<p><a href=\"{WebUtility.HtmlEncode(fullUrl)}\" style=\"color:#d81159;font-weight:bold;\">View details</a></p>";
        }

        string body =
            "<div style=\"font-family:Arial,sans-serif;color:#2b2d42;max-width:560px;\">" +
            $"<h2 style=\"color:#d81159;margin-bottom:4px;\">{encodedTitle}</h2>" +
            $"<p>{encodedMessage}</p>" +
            linkHtml +
            $"<p style=\"color:#5c5f77;font-size:12px;\">{WebUtility.HtmlEncode(SiteInfo.RestaurantName)} &middot; {WebUtility.HtmlEncode(SiteInfo.Tagline)}</p>" +
            "</div>";

        return new EmailMessage(toAddress, $"[{SiteInfo.RestaurantName}] {title}", body);
    }

    // ---------------------------------------------------------------------
    // Reading (INotificationInbox)
    // ---------------------------------------------------------------------

    public async Task<IReadOnlyList<NotificationItem>> GetRecentAsync(string userId, int count)
    {
        return await _context.Notifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId)
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .ThenByDescending(notification => notification.Id)
            .Take(count)
            .Select(notification => new NotificationItem
            {
                Id = notification.Id,
                Type = notification.Type,
                Title = notification.Title,
                Message = notification.Message,
                LinkUrl = notification.LinkUrl,
                IsRead = notification.IsRead,
                CreatedAtUtc = notification.CreatedAtUtc
            })
            .ToListAsync();
    }

    public async Task<int> GetUnreadCountAsync(string userId)
    {
        return await _context.Notifications
            .CountAsync(notification => notification.UserId == userId && !notification.IsRead);
    }

    public async Task<NotificationItem?> MarkAsReadAsync(string userId, int notificationId)
    {
        // Filtering by BOTH id and owner is the ownership check: another user's ID simply finds nothing.
        Notification? notification = await _context.Notifications
            .FirstOrDefaultAsync(item => item.Id == notificationId && item.UserId == userId);

        if (notification == null)
        {
            return null;
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return new NotificationItem
        {
            Id = notification.Id,
            Type = notification.Type,
            Title = notification.Title,
            Message = notification.Message,
            LinkUrl = notification.LinkUrl,
            IsRead = notification.IsRead,
            CreatedAtUtc = notification.CreatedAtUtc
        };
    }

    public async Task<int> MarkAllAsReadAsync(string userId)
    {
        DateTime now = DateTime.UtcNow;

        // ExecuteUpdateAsync runs one UPDATE statement in the database, without loading every row first.
        return await _context.Notifications
            .Where(notification => notification.UserId == userId && !notification.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(notification => notification.IsRead, true)
                .SetProperty(notification => notification.ReadAtUtc, now));
    }

    //Cuts text to a maximum length so it always fits its database column
    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, maxLength);
    }

    //The minimum we need to know about a recipient
    private class Recipient
    {
        public string UserId { get; set; } = string.Empty;

        public string? Email { get; set; }
    }
}
