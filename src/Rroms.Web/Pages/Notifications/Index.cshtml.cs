using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Rroms.Web.Models.ViewModels;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.Pages.Notifications;

/// <summary>
/// Notification inbox for every signed-in user (YY-6).
/// Protected by AuthorizeFolder("/Notifications") in Program.cs.
/// </summary>
public class IndexModel : PageModel
{
    //How many recent notifications the inbox shows
    public const int PageSize = 50;

    private readonly INotificationInbox _inbox;

    //Receives the inbox service from dependency injection
    public IndexModel(INotificationInbox inbox)
    {
        _inbox = inbox;
    }

    //The user's recent notifications, newest first
    public IReadOnlyList<NotificationItem> Notifications { get; private set; } = new List<NotificationItem>();

    //Number of unread notifications
    public int UnreadCount { get; private set; }

    //Shows the inbox
    public async Task OnGetAsync()
    {
        string userId = User.GetUserId();
        Notifications = await _inbox.GetRecentAsync(userId, PageSize);
        UnreadCount = await _inbox.GetUnreadCountAsync(userId);
    }

    /// <summary>
    /// GET /Notifications?handler=UnreadCount - small JSON endpoint used by notifications.js
    /// when the live connection is unavailable (polling fallback).
    /// </summary>
    public async Task<IActionResult> OnGetUnreadCountAsync()
    {
        int unreadCount = await _inbox.GetUnreadCountAsync(User.GetUserId());
        return new JsonResult(new { unreadCount });
    }

    /// <summary>
    /// Marks a notification as read and opens its link. A POST, so it is protected by the antiforgery token.
    /// </summary>
    public async Task<IActionResult> OnPostOpenAsync(int id)
    {
        NotificationItem? notification = await _inbox.MarkAsReadAsync(User.GetUserId(), id);
        if (notification == null)
        {
            // Not found or someone else's: the same 404 either way, so IDs cannot be probed.
            return NotFound();
        }

        // Only follow links inside this site (same open-redirect protection as the login page).
        if (!string.IsNullOrEmpty(notification.LinkUrl) && Url.IsLocalUrl(notification.LinkUrl))
        {
            return LocalRedirect(notification.LinkUrl);
        }

        return RedirectToPage();
    }

    //Marks every notification as read
    public async Task<IActionResult> OnPostMarkAllReadAsync()
    {
        int changed = await _inbox.MarkAllAsReadAsync(User.GetUserId());
        TempData["StatusMessage"] = changed == 0
            ? "You're all caught up."
            : $"Marked {changed} notification(s) as read.";
        return RedirectToPage();
    }
}
