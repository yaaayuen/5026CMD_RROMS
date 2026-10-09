using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;

namespace Rroms.Tests.Notifications;

/// <summary>
/// YY-6 acceptance: notifications are stored, pushed live and emailed; users only see their own. Rubric item 5.
/// </summary>
public class NotificationTests : IClassFixture<TestWebApplicationFactory>
{
    private const string Password = "Grill!Test2026";

    private readonly TestWebApplicationFactory _factory;

    public NotificationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string UniqueEmail()
    {
        return $"notify-{Guid.NewGuid():N}@threegrills.test";
    }

    private async Task NotifyAsync(string userId, string title, string? link = null)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        INotificationService notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        await notifications.NotifyUserAsync(userId, NotificationType.ReservationConfirmed, title, "Your table is confirmed.", link);
    }

    private async Task<IReadOnlyList<NotificationItem>> InboxOfAsync(string userId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        INotificationInbox inbox = scope.ServiceProvider.GetRequiredService<INotificationInbox>();
        return await inbox.GetRecentAsync(userId, 50);
    }

    [Fact]
    public async Task NotifyUser_StoresInInbox_PushesLive_AndQueuesEmail()
    {
        string email = UniqueEmail();
        ApplicationUser user = await _factory.CreateUserAsync(email, Password, RoleNames.Customer);
        string title = $"Confirmed {Guid.NewGuid():N}";

        await NotifyAsync(user.Id, title, "/Customer");

        IReadOnlyList<NotificationItem> inbox = await InboxOfAsync(user.Id);
        Assert.Contains(inbox, item => item.Title == title && !item.IsRead);
        Assert.True(_factory.Pusher.WasPushedTo(user.Id, title));
        Assert.Contains(_factory.EmailQueue.Queued, message => message.ToAddress == email && message.Subject.Contains(title));
    }

    [Fact]
    public async Task InboxPage_OpeningSomeoneElsesNotification_Returns404()
    {
        ApplicationUser alice = await _factory.CreateUserAsync(UniqueEmail(), Password, RoleNames.Customer);
        string bobEmail = UniqueEmail();
        await _factory.CreateUserAsync(bobEmail, Password, RoleNames.Customer);
        await NotifyAsync(alice.Id, "Private to Alice");
        int aliceNotificationId = (await InboxOfAsync(alice.Id)).Single().Id;

        HttpClient bob = _factory.CreateSecureClient();
        await FormHelper.LoginAsync(bob, bobEmail, Password);
        HttpResponseMessage response = await FormHelper.PostFormAsync(
            bob, "/Notifications", $"/Notifications?handler=Open&id={aliceNotificationId}", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
