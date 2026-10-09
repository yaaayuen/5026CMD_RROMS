using Microsoft.AspNetCore.SignalR;

using Rroms.Web.Hubs;

namespace Rroms.Web.Services;

/// <summary>
/// Sends live notifications through the <see cref="NotificationHub"/> (YY-6).
/// </summary>
public class SignalRNotificationPusher : INotificationPusher
{
    private readonly IHubContext<NotificationHub> _hubContext;

    //IHubContext lets code outside the hub (like this service) send messages to connected clients.

    public SignalRNotificationPusher(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task PushAsync(IReadOnlyList<string> userIds, NotificationPushPayload payload)
    {
        if (userIds.Count == 0)
        {
            return;
        }

        // Clients.Users targets connections by Identity user ID; users who are offline simply receive nothing
        // and will see the notification in their inbox next time.
        await _hubContext.Clients.Users(userIds).SendAsync(NotificationHub.NotificationReceivedMethod, payload);
    }
}
