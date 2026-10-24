using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rroms.Web.Contracts;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Options;
using Rroms.Web.Services;
namespace Rroms.Tests.DiningExperience;

/// <summary>Part 13: feedback replies, hiding and deleting comments, the rating summary, scheduled promotions and dish links.</summary>
public class FeedbackModerationAndScheduleTests : IDisposable
{
    private readonly SqliteConnection _connection = new SqliteConnection("DataSource=:memory:");
    private readonly ApplicationDbContext _db;
    private readonly Notices _notices = new Notices();
    private readonly FeedbackWordFilter _words = new FeedbackWordFilter(Microsoft.Extensions.Options.Options.Create(new FeedbackOptions { BlockedWords = new[] { "rubbish" } }));
    public FeedbackModerationAndScheduleTests()
    {
        _connection.Open();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.Users.Add(new ApplicationUser { Id = "alice", UserName = "alice@test.local", FullName = "Alice Tan" });
        _db.Users.Add(new ApplicationUser { Id = "bob", UserName = "bob@test.local", FullName = "Bob Lee" });
        _db.SaveChanges();
    }
    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    private async Task<Feedback> AddFeedback(string customerId, int rating, string comment, int daysAgo = 0)
    {
        var reservation = new Reservation { CustomerId = customerId, DiningTableId = 1, PartySize = 2, Status = ReservationStatus.Completed,
            StartUtc = DateTime.UtcNow.AddDays(-daysAgo - 1), EndUtc = DateTime.UtcNow.AddDays(-daysAgo - 1).AddHours(2) };
        var feedback = new Feedback { Reservation = reservation, Rating = rating, Comment = comment, CreatedAtUtc = DateTime.UtcNow.AddDays(-daysAgo) };
        _db.Feedback.Add(feedback);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return feedback;
    }

    [Fact]
    public async Task ReplyIsSavedAndNotifiesTheCustomerOnlyTheFirstTime()
    {
        var feedback = await AddFeedback("alice", 4, "Lovely lamb");
        var service = new FeedbackService(_db, _notices, _words);
        Assert.False((await service.ReplyAsync(feedback.Id, "   ")).Succeeded);
        Assert.False((await service.ReplyAsync(feedback.Id, new string('x', 1001))).Succeeded);
        Assert.False((await service.ReplyAsync(999, "Thanks")).Succeeded);
        Assert.True((await service.ReplyAsync(feedback.Id, " Thank you, Alice! ")).Succeeded);
        Assert.True((await service.ReplyAsync(feedback.Id, "Thank you again")).Succeeded);
        Assert.Equal(new[] { "alice" }, _notices.Users);
        Assert.Equal("/Customer/Feedback", _notices.Links.Single());
        var mine = Assert.Single(await service.GetMineAsync("alice"));
        Assert.Equal("Thank you again", mine.Reply);
        Assert.NotNull(mine.RepliedAtUtc);
        Assert.Empty(await service.GetMineAsync("bob"));
    }

    [Fact]
    public async Task HiddenAndDeletedCommentsNeverReachTheWebsite()
    {
        var good = await AddFeedback("alice", 5, "Best ribeye in town");
        var rude = await AddFeedback("bob", 1, "Rude words here");
        var removed = await AddFeedback("bob", 2, "Spam link");
        await AddFeedback("alice", 3, "");
        var service = new FeedbackService(_db, _notices, _words);
        Assert.True((await service.SetHiddenAsync(rude.Id, true)).Succeeded);
        Assert.True((await service.RemoveCommentAsync(removed.Id)).Succeeded);
        Assert.False((await service.SetHiddenAsync(999, true)).Succeeded);
        Assert.False((await service.RemoveCommentAsync(999)).Succeeded);
        var shown = Assert.Single(await service.GetPublicAsync(6));
        Assert.Equal(good.Id, shown.Id);
        Assert.Equal("Alice Tan", shown.Reservation!.Customer!.FullName);
        _db.ChangeTracker.Clear();
        var deleted = await _db.Feedback.FindAsync(removed.Id);
        Assert.True(deleted!.CommentRemoved);
        Assert.Equal("", deleted.Comment);
        Assert.Equal(2, deleted.Rating);
        // Showing the review again brings it back.
        Assert.True((await service.SetHiddenAsync(rude.Id, false)).Succeeded);
        Assert.Equal(2, (await service.GetPublicAsync(6)).Count);
    }

    [Theory]
    [InlineData("This place is shit", "shit")]
    [InlineData("SH1T service", "shit")]
    [InlineData("shiiiit food!", "shit")]
    [InlineData("What a $hit night.", "shit")]
    [InlineData("fucking slow", "fuck")]
    [InlineData("Staff were bodoh", "bodoh")]
    [InlineData("Total rubbish", "rubbish")]
    [InlineData("You asshole.", "asshole")]
    public void WordFilterCatchesBlockedWordsAndSimpleDisguises(string comment, string word)
    {
        Assert.Contains(word, _words.Find(comment));
    }

    [Theory]
    [InlineData("We read Dickens while waiting")]
    [InlineData("Scunthorpe guests loved it")]
    [InlineData("Shiitake mushrooms were great")]
    [InlineData("A special night, very social staff")]
    [InlineData("")]
    [InlineData(null)]
    public void WordFilterLeavesCleanCommentsAlone(string? comment)
    {
        Assert.Empty(_words.Find(comment));
    }

    [Fact]
    public async Task ReviewWithABlockedWordIsHiddenAndFlaggedAutomatically()
    {
        var service = new FeedbackService(_db, _notices, _words);
        DateTime start = DateTime.UtcNow.AddDays(-1);
        var rude = new Reservation { CustomerId = "alice", DiningTableId = 1, PartySize = 2, Status = ReservationStatus.Completed, StartUtc = start, EndUtc = start.AddHours(2) };
        var clean = new Reservation { CustomerId = "alice", DiningTableId = 1, PartySize = 2, Status = ReservationStatus.Completed, StartUtc = start, EndUtc = start.AddHours(2) };
        _db.Reservations.AddRange(rude, clean);
        await _db.SaveChangesAsync();
        Assert.True((await service.SubmitAsync("alice", rude.Id, 1, "Sh1t food")).Succeeded);
        Assert.True((await service.SubmitAsync("alice", clean.Id, 5, "Lovely evening")).Succeeded);
        var flagged = Assert.Single(await service.GetAsync(null, null, null, FeedbackView.Flagged));
        Assert.True(flagged.IsHidden);
        Assert.Equal(rude.Id, flagged.ReservationId);
        Assert.Equal(new[] { "shit" }, service.BlockedWordsIn(flagged.Comment, Array.Empty<string>()));
        Assert.Equal("Lovely evening", Assert.Single(await service.GetPublicAsync(6)).Comment);
        Assert.Equal(1, (await service.GetSummaryAsync(1)).FlaggedCount);
        // The admin can still decide to show it.
        Assert.True((await service.SetHiddenAsync(flagged.Id, false)).Succeeded);
        Assert.Equal(2, (await service.GetPublicAsync(6)).Count);
    }

    [Fact]
    public async Task AdminWordsAreValidatedAndBlockNewReviewsStraightAway()
    {
        var service = new FeedbackService(_db, _notices, _words);
        Assert.False((await service.AddBlockedWordAsync(" ")).Succeeded);
        Assert.False((await service.AddBlockedWordAsync("a")).Succeeded);
        Assert.False((await service.AddBlockedWordAsync("bad(word")).Succeeded);
        Assert.False((await service.AddBlockedWordAsync(new string('x', 41))).Succeeded);
        Assert.False((await service.AddBlockedWordAsync("SHIT")).Succeeded);
        Assert.True((await service.AddBlockedWordAsync("  Disgusting  ")).Succeeded);
        Assert.False((await service.AddBlockedWordAsync("disgusting")).Succeeded);
        var word = Assert.Single(await service.GetCustomBlockedWordsAsync());
        Assert.Equal("disgusting", word.Word);
        Assert.Contains("shit", service.FixedBlockedWords);

        DateTime start = DateTime.UtcNow.AddDays(-1);
        var visit = new Reservation { CustomerId = "alice", DiningTableId = 1, PartySize = 2, Status = ReservationStatus.Completed, StartUtc = start, EndUtc = start.AddHours(2) };
        _db.Reservations.Add(visit);
        await _db.SaveChangesAsync();
        Assert.True((await service.SubmitAsync("alice", visit.Id, 1, "DISGUSTING tables")).Succeeded);
        Assert.Single(await service.GetAsync(null, null, null, FeedbackView.Flagged));

        Assert.True((await service.RemoveBlockedWordAsync(word.Id)).Succeeded);
        Assert.False((await service.RemoveBlockedWordAsync(word.Id)).Succeeded);
        Assert.Empty(await service.GetCustomBlockedWordsAsync());
    }

    [Fact]
    public async Task RecheckFlagsOlderReviewsButLeavesReviewsTheAdminChoseToShow()
    {
        var old = await AddFeedback("alice", 2, "The soup was awful");
        var clean = await AddFeedback("bob", 5, "Lovely soup");
        var shown = await AddFeedback("bob", 1, "awful again");
        var service = new FeedbackService(_db, _notices, _words);
        await _db.Feedback.Where(f => f.Id == shown.Id).ExecuteUpdateAsync(s => s.SetProperty(f => f.IsFlagged, true));
        Assert.Equal(0, await service.RecheckAsync());
        Assert.True((await service.AddBlockedWordAsync("awful")).Succeeded);
        Assert.Equal(1, await service.RecheckAsync());
        Assert.Equal(0, await service.RecheckAsync());
        var flagged = await service.GetAsync(null, null, null, FeedbackView.Flagged);
        Assert.Contains(flagged, f => f.Id == old.Id && f.IsHidden);
        var publicIds = (await service.GetPublicAsync(6)).Select(f => f.Id).ToList();
        Assert.Contains(clean.Id, publicIds);
        Assert.Contains(shown.Id, publicIds);
        Assert.DoesNotContain(old.Id, publicIds);
    }

    [Fact]
    public async Task FiltersByVisibilityReplyAndCommentText()
    {
        var first = await AddFeedback("alice", 5, "Great SERVICE and food");
        var second = await AddFeedback("bob", 2, "Slow service");
        await AddFeedback("bob", 4, "Nice desserts");
        var service = new FeedbackService(_db, _notices, _words);
        await service.SetHiddenAsync(second.Id, true);
        await service.ReplyAsync(first.Id, "Thanks");
        Assert.Equal(2, (await service.GetAsync(null, null, null, FeedbackView.All, "service")).Count);
        Assert.Equal(second.Id, Assert.Single(await service.GetAsync(null, null, null, FeedbackView.Hidden)).Id);
        Assert.Equal(2, (await service.GetAsync(null, null, null, FeedbackView.Shown)).Count);
        Assert.Equal(first.Id, Assert.Single(await service.GetAsync(null, null, null, FeedbackView.Replied)).Id);
        Assert.Equal(2, (await service.GetAsync(null, null, null, FeedbackView.AwaitingReply)).Count);
        Assert.Empty(await service.GetAsync(5, null, null, FeedbackView.Hidden));
    }

    [Fact]
    public async Task SummaryCountsStarsAndGroupsRecentWeeks()
    {
        await AddFeedback("alice", 5, "a");
        await AddFeedback("alice", 4, "b");
        var hidden = await AddFeedback("bob", 1, "c", daysAgo: 7);
        await AddFeedback("bob", 3, "old", daysAgo: 120);
        var service = new FeedbackService(_db, _notices, _words);
        await service.SetHiddenAsync(hidden.Id, true);
        FeedbackSummary summary = await service.GetSummaryAsync(8);
        Assert.Equal(4, summary.Count);
        Assert.Equal(3.25, summary.Average);
        Assert.Equal(new[] { 1, 0, 1, 1, 1 }, summary.StarCounts);
        Assert.Equal(1, summary.HiddenCount);
        Assert.Equal(4, summary.AwaitingReplyCount);
        Assert.Equal(8, summary.Weeks.Count);
        Assert.All(summary.Weeks, w => Assert.Equal(DayOfWeek.Monday, w.StartLocal.DayOfWeek));
        // The 120-day-old review is outside the eight weeks; the other three are inside.
        Assert.Equal(3, summary.Weeks.Sum(w => w.Count));
        // Today's two reviews are in the current week; the one from seven days ago is always in the week before.
        Assert.Equal(2, summary.Weeks[^1].Count);
        Assert.Equal(4.5, summary.Weeks[^1].Average);
        Assert.Equal(1, summary.Weeks[^2].Count);
        Assert.Null(summary.Weeks[0].Average);
    }

    [Fact]
    public async Task FuturePromotionIsAnnouncedOnceWhenItStarts()
    {
        var service = new AnnouncementService(_db, _notices);
        var item = new Announcement { Title = "Weekend brunch", Message = "Starts Saturday", IsPromotion = true, IsPublished = true,
            StartUtc = DateTime.UtcNow.AddDays(2), EndUtc = DateTime.UtcNow.AddDays(9) };
        Assert.True((await service.SaveAsync(item)).Succeeded);
        Assert.Empty(_notices.Roles);
        Assert.Empty(await service.GetAsync(true));
        Assert.Equal(0, await service.NotifyDueAsync());
        // The start time arrives.
        await _db.Announcements.ExecuteUpdateAsync(s => s.SetProperty(a => a.StartUtc, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Equal(1, await service.NotifyDueAsync());
        Assert.Equal(0, await service.NotifyDueAsync());
        Assert.Single(_notices.Roles);
        // Unpublishing and publishing again does not repeat the notification.
        var saved = Assert.Single(await service.GetAsync(false));
        Assert.NotNull(saved.NotifiedAtUtc);
        saved.IsPublished = false; Assert.True((await service.SaveAsync(saved)).Succeeded);
        saved.IsPublished = true; Assert.True((await service.SaveAsync(saved)).Succeeded);
        Assert.Single(_notices.Roles);
    }

    [Fact]
    public async Task NewsAndDraftsAreNeverAnnouncedAndEndedPromotionsAreSkipped()
    {
        var service = new AnnouncementService(_db, _notices);
        DateTime now = DateTime.UtcNow;
        Assert.True((await service.SaveAsync(new Announcement { Title = "News", Message = "m", IsPublished = true,
            StartUtc = now.AddHours(-1), EndUtc = now.AddDays(1) })).Succeeded);
        Assert.True((await service.SaveAsync(new Announcement { Title = "Draft", Message = "m", IsPromotion = true,
            StartUtc = now.AddHours(-1), EndUtc = now.AddDays(1) })).Succeeded);
        _db.Announcements.Add(new Announcement { Title = "Over", Message = "m", IsPromotion = true, IsPublished = true,
            StartUtc = now.AddDays(-3), EndUtc = now.AddDays(-1), UpdatedAtUtc = now });
        await _db.SaveChangesAsync();
        Assert.Equal(0, await service.NotifyDueAsync());
        Assert.Empty(_notices.Roles);
    }

    [Fact]
    public async Task LinkedDishOpensTheMenuAndDeletingTheDishKeepsThePost()
    {
        var category = new MenuCategory { Name = "Test grills", DisplayOrder = 1 };
        var dish = new MenuItem { Name = "Smoky Lamb & Rice", Description = "d", MenuCategory = category, Price = 30m,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        _db.MenuItems.Add(dish);
        await _db.SaveChangesAsync();
        var service = new AnnouncementService(_db, _notices);
        DateTime now = DateTime.UtcNow;
        Assert.Contains(await service.GetDishChoicesAsync(), m => m.Id == dish.Id);
        Assert.False((await service.SaveAsync(new Announcement { Title = "Bad", Message = "m", MenuItemId = 999,
            StartUtc = now, EndUtc = now.AddDays(1) })).Succeeded);
        Assert.True((await service.SaveAsync(new Announcement { Title = "New dish", Message = "Try it", IsPromotion = true, IsPublished = true,
            MenuItemId = dish.Id, StartUtc = now.AddMinutes(-1), EndUtc = now.AddDays(7) })).Succeeded);
        Assert.Equal("/Menu?q=Smoky%20Lamb%20%26%20Rice", _notices.Links.Single());
        Assert.Equal("Smoky Lamb & Rice", Assert.Single(await service.GetAsync(true)).MenuItem!.Name);
        _db.ChangeTracker.Clear();
        _db.MenuItems.Remove((await _db.MenuItems.FindAsync(dish.Id))!);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var post = Assert.Single(await service.GetAsync(true));
        Assert.Null(post.MenuItemId);
        Assert.Equal("/Announcements", AnnouncementService.LinkFor(post));
    }

    private class Notices : INotificationService
    {
        public List<string> Users { get; } = new List<string>();
        public List<string> Roles { get; } = new List<string>();
        public List<string?> Links { get; } = new List<string?>();
        public Task NotifyUserAsync(string userId, NotificationType type, string title, string message, string? linkUrl = null)
        { Users.Add(userId); Links.Add(linkUrl); return Task.CompletedTask; }
        public Task NotifyRoleAsync(string roleName, NotificationType type, string title, string message, string? linkUrl = null)
        { Roles.Add(roleName); Links.Add(linkUrl); return Task.CompletedTask; }
    }
}
