namespace Rroms.Web.Services;

/// <summary>
/// Pushes a notification to users' open browser tabs in real time (YY-6).
/// </summary>

public interface INotificationPusher
{
    //Sends the payload to every connected tab of the given users
    Task PushAsync(IReadOnlyList<string> userIds, NotificationPushPayload payload);
}
