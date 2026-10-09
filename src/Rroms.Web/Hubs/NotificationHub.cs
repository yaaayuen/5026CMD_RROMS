using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Rroms.Web.Hubs;

/// <summary>
/// SignalR hub that keeps an open connection to each signed-in browser,
/// so the server can push notifications instantly instead of the page polling (YY-6).
/// </summary>

[Authorize]
public class NotificationHub : Hub
{
    //URL the browser connects to
    public const string Path = "/hubs/notifications";

    //Name of the client-side event raised for a new notification
    public const string NotificationReceivedMethod = "notificationReceived";
}
