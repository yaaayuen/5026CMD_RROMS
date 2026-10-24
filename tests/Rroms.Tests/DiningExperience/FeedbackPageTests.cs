using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.History;
using Rroms.Tests.Infrastructure;
using Rroms.Web.Constants;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
namespace Rroms.Tests.DiningExperience;

/// <summary>Part 13 through the real pages: guest reviews on News and offers, admin moderation and announcement tabs.</summary>
public class FeedbackPageTests
{
    [Fact]
    public async Task WebsiteShowsVisibleReviewsWithFirstNameAndReplyButNeverHiddenOnes()
    {
        using var factory = new TestWebApplicationFactory();
        ApplicationUser guest = await factory.CreateUserAsync("alex-review@test.local", "Test!Customer2026", RoleNames.Customer);
        await AddFeedbackAsync(factory, guest.Id, 5, "Wonderful smoky ribeye", reply: "See you again soon");
        await AddFeedbackAsync(factory, guest.Id, 1, "Hidden rude remark", hidden: true);
        await AddFeedbackAsync(factory, guest.Id, 2, "", removed: true);
        using var client = factory.CreateSecureClient();
        string html = await client.GetStringAsync("/Announcements");
        Assert.Contains("What guests say", html);
        Assert.Contains("Wonderful smoky ribeye", html);
        Assert.Contains("See you again soon", html);
        Assert.Contains("aria-label=\"5 out of 5 stars\"", html);
        Assert.Contains(">Test · ", html);
        Assert.DoesNotContain("Test User", html);
        Assert.DoesNotContain("Hidden rude remark", html);
        // The average still counts every rating, including hidden ones.
        Assert.Contains("2.7 out of 5 from 3 reviews", html);
    }

    [Fact]
    public async Task AdminCanReplyHideAndDeleteACommentAndTheCustomerSeesTheReply()
    {
        using var factory = new TestWebApplicationFactory();
        ApplicationUser guest = await factory.CreateUserAsync("alex-moderate@test.local", "Test!Customer2026", RoleNames.Customer);
        int id = await AddFeedbackAsync(factory, guest.Id, 1, "Terrible spam comment");
        using var admin = factory.CreateSecureClient();
        await FormHelper.LoginAsync(admin, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        string page = await admin.GetStringAsync("/Admin/Feedback");
        Assert.Contains("All-time average", page);
        Assert.Contains("Reviews by stars", page);
        Assert.Contains("Awaiting reply", page);

        var replied = await FormHelper.PostFormAsync(admin, "/Admin/Feedback", $"/Admin/Feedback?handler=Reply&id={id}&View=Hidden",
            new Dictionary<string, string> { ["reply"] = "Sorry to hear that" });
        Assert.Equal(HttpStatusCode.Redirect, replied.StatusCode);
        Assert.Contains("View=Hidden", replied.Headers.Location!.ToString());
        var hidden = await FormHelper.PostFormAsync(admin, "/Admin/Feedback", $"/Admin/Feedback?handler=Hide&id={id}&hidden=true",
            new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, hidden.StatusCode);
        Assert.DoesNotContain("Terrible spam comment", await admin.GetStringAsync("/Announcements"));
        Assert.Contains("Terrible spam comment", await admin.GetStringAsync("/Admin/Feedback?View=Hidden"));
        Assert.DoesNotContain("Terrible spam comment", await admin.GetStringAsync("/Admin/Feedback?View=Shown"));
        var removed = await FormHelper.PostFormAsync(admin, "/Admin/Feedback", $"/Admin/Feedback?handler=RemoveComment&id={id}",
            new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        Assert.Contains("Comment deleted by an administrator.", await admin.GetStringAsync("/Admin/Feedback"));

        using var customer = factory.CreateSecureClient();
        await FormHelper.LoginAsync(customer, "alex-moderate@test.local", "Test!Customer2026");
        string mine = await customer.GetStringAsync("/Customer/Feedback");
        Assert.Contains("Your reviews", mine);
        Assert.Contains("Sorry to hear that", mine);
        Assert.Contains("Your comment was removed by the restaurant.", mine);
        Assert.DoesNotContain("Terrible spam comment", mine);
    }

    [Fact]
    public async Task AdminAddsAWordAndReCheckHidesAnOlderReview()
    {
        using var factory = new TestWebApplicationFactory();
        ApplicationUser guest = await factory.CreateUserAsync("alex-words@test.local", "Test!Customer2026", RoleNames.Customer);
        await AddFeedbackAsync(factory, guest.Id, 2, "Service was horrid tonight");
        using var admin = factory.CreateSecureClient();
        await FormHelper.LoginAsync(admin, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        string page = await admin.GetStringAsync("/Admin/Feedback");
        Assert.Contains("Blocked words", page);
        Assert.Contains("Built-in words", page);
        Assert.Contains("Service was horrid tonight", await admin.GetStringAsync("/Announcements"));
        var added = await FormHelper.PostFormAsync(admin, "/Admin/Feedback", "/Admin/Feedback?handler=AddWord",
            new Dictionary<string, string> { ["word"] = "Horrid" });
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        Assert.Contains(">horrid</span>", await admin.GetStringAsync("/Admin/Feedback"));
        var recheck = await FormHelper.PostFormAsync(admin, "/Admin/Feedback", "/Admin/Feedback?handler=Recheck", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, recheck.StatusCode);
        string flagged = await admin.GetStringAsync("/Admin/Feedback?View=Flagged");
        Assert.Contains("Service was horrid tonight", flagged);
        Assert.Contains("Flagged by word filter: horrid", flagged);
        Assert.DoesNotContain("Service was horrid tonight", await admin.GetStringAsync("/Announcements"));
    }

    [Fact]
    public async Task ModerationNeedsAnAdministratorAndAToken()
    {
        using var factory = new TestWebApplicationFactory();
        ApplicationUser guest = await factory.CreateUserAsync("alex-nomod@test.local", "Test!Customer2026", RoleNames.Customer);
        int id = await AddFeedbackAsync(factory, guest.Id, 5, "Keep me");
        using var admin = factory.CreateSecureClient();
        await FormHelper.LoginAsync(admin, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        var noToken = await admin.PostAsync($"/Admin/Feedback?handler=Hide&id={id}&hidden=true", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var customer = factory.CreateSecureClient();
        await FormHelper.LoginAsync(customer, "alex-nomod@test.local", "Test!Customer2026");
        var denied = await customer.PostAsync($"/Admin/Feedback?handler=Hide&id={id}&hidden=true", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
        Assert.Contains("Keep me", await customer.GetStringAsync("/Announcements"));
    }

    [Fact]
    public async Task AdminTabsSeparateScheduledLiveDraftAndEndedPosts()
    {
        using var factory = new TestWebApplicationFactory();
        DateTime now = DateTime.UtcNow;
        await AddPostAsync(factory, "Tab scheduled", true, now.AddDays(2), now.AddDays(9));
        await AddPostAsync(factory, "Tab live", true, now.AddDays(-1), now.AddDays(5));
        await AddPostAsync(factory, "Tab draft", false, now.AddDays(-1), now.AddDays(5));
        await AddPostAsync(factory, "Tab ended", true, now.AddDays(-9), now.AddDays(-2));
        using var admin = factory.CreateSecureClient();
        await FormHelper.LoginAsync(admin, TestWebApplicationFactory.AdminEmail, TestWebApplicationFactory.AdminPassword);
        string upcoming = await admin.GetStringAsync("/Admin/Announcements?show=upcoming");
        Assert.Contains("Tab scheduled", upcoming);
        Assert.Contains(">Scheduled</span>", upcoming);
        Assert.DoesNotContain("Tab live", upcoming);
        string all = await admin.GetStringAsync("/Admin/Announcements");
        foreach (string title in new[] { "Tab scheduled", "Tab live", "Tab draft", "Tab ended" }) { Assert.Contains(title, all); }
        Assert.DoesNotContain("Tab live", await admin.GetStringAsync("/Admin/Announcements?show=ended"));
        Assert.Contains("Tab draft", await admin.GetStringAsync("/Admin/Announcements?show=drafts"));
        Assert.Contains("Linked dish (optional)", all);
        // The public page never shows the scheduled post before it starts.
        Assert.DoesNotContain("Tab scheduled", await admin.GetStringAsync("/Announcements"));
    }

    private static async Task<int> AddFeedbackAsync(TestWebApplicationFactory factory, string customerId, int rating, string comment,
        string? reply = null, bool hidden = false, bool removed = false)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Reservation visit = await HistoryTestData.AddReservationAsync(context, customerId, DateTime.UtcNow.AddDays(-2), ReservationStatus.Completed);
        var feedback = new Feedback { ReservationId = visit.Id, Rating = rating, Comment = comment, CreatedAtUtc = DateTime.UtcNow,
            Reply = reply, RepliedAtUtc = reply == null ? null : DateTime.UtcNow, IsHidden = hidden, CommentRemoved = removed };
        context.Feedback.Add(feedback);
        await context.SaveChangesAsync();
        return feedback.Id;
    }

    private static async Task AddPostAsync(TestWebApplicationFactory factory, string title, bool published, DateTime startUtc, DateTime endUtc)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Announcements.Add(new Announcement { Title = title, Message = "Made for a test", IsPromotion = true, IsPublished = published,
            StartUtc = startUtc, EndUtc = endUtc, UpdatedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();
    }
}
