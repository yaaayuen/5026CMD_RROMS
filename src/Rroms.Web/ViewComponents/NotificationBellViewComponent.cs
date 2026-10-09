using Microsoft.AspNetCore.Mvc;
using Rroms.Web.Security;
using Rroms.Web.Services;

namespace Rroms.Web.ViewComponents;

/// <summary>
/// The bell icon with an unread-count badge in the navbar (YY-6).
/// </summary>

public class NotificationBellViewComponent : ViewComponent
{
    private readonly INotificationInbox _inbox;

    //Receives the inbox service from dependency injection
    public NotificationBellViewComponent(INotificationInbox inbox)
    {
        _inbox = inbox;
    }

    //Renders the bell for signed-in users, and nothing for visitors
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (UserClaimsPrincipal.Identity?.IsAuthenticated != true)
        {
            return Content(string.Empty);
        }

        int unreadCount = await _inbox.GetUnreadCountAsync(UserClaimsPrincipal.GetUserId());
        return View(unreadCount);
    }
}