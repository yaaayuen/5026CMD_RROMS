using System.Collections.Concurrent;
using Rroms.Web.Services;

namespace Rroms.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="INotificationPusher"/>: instead of using SignalR,
/// it records every push so tests can check who would have received what.
/// </summary>
public class RecordingNotificationPusher : INotificationPusher
{
    //Every push made, in order
    public ConcurrentQueue<RecordedPush> Pushes { get; } = new ConcurrentQueue<RecordedPush>();

    public Task PushAsync(IReadOnlyList<string> userIds, NotificationPushPayload payload)
    {
        Pushes.Enqueue(new RecordedPush(userIds.ToList(), payload));
        return Task.CompletedTask;
    }

    //True if any push reached the given user with the given title
    public bool WasPushedTo(string userId, string title)
    {
        return Pushes.Any(push => push.UserIds.Contains(userId) && push.Payload.Title == title);
    }
}

//One recorded push
public class RecordedPush
{
    public RecordedPush(List<string> userIds, NotificationPushPayload payload)
    {
        UserIds = userIds;
        Payload = payload;
    }

    public List<string> UserIds { get; }

    public NotificationPushPayload Payload { get; }
}
