using Microsoft.Extensions.DependencyInjection;
using Rroms.Tests.Infrastructure;
using Rroms.Web.Data;
using Rroms.Web.Models;
namespace Rroms.Tests.DiningExperience;

/// <summary>Checks the public News and offers page: the tabs, the badges and that only active posts appear.</summary>
public class AnnouncementPageTests
{
    [Fact]
    public async Task OffersTabShowsOnlyOffersWithABookingButton()
    {
        using var factory = new TestWebApplicationFactory();
        await AddAsync(factory, "Weekday lunch set", promotion: true);
        await AddAsync(factory, "Holiday opening hours", promotion: false);
        using var client = factory.CreateSecureClient();
        string offers = await client.GetStringAsync("/Announcements?show=offers");
        Assert.Contains("Weekday lunch set", offers);
        Assert.DoesNotContain("Holiday opening hours", offers);
        Assert.Contains(">Offer</span>", offers);
        Assert.Contains("/Customer/Reservations/Book", offers);
        Assert.Contains("aria-current=\"page\"", offers);
        string news = await client.GetStringAsync("/Announcements?show=news");
        Assert.Contains("Holiday opening hours", news);
        Assert.DoesNotContain("Weekday lunch set", news);
        Assert.Contains(">News</span>", news);
    }
    [Fact]
    public async Task UnknownTabShowsEverythingActiveAndHidesDraftsAndExpiredPosts()
    {
        using var factory = new TestWebApplicationFactory();
        await AddAsync(factory, "Running offer", promotion: true);
        await AddAsync(factory, "Running news", promotion: false);
        await AddAsync(factory, "Unpublished draft", promotion: true, published: false);
        await AddAsync(factory, "Finished campaign", promotion: true, endsInDays: -1);
        using var client = factory.CreateSecureClient();
        string html = await client.GetStringAsync("/Announcements?show=<script>");
        Assert.Contains("Running offer", html);
        Assert.Contains("Running news", html);
        Assert.DoesNotContain("Unpublished draft", html);
        Assert.DoesNotContain("Finished campaign", html);
        Assert.DoesNotContain("<script>", html);
    }
    [Fact]
    public async Task PostsEndingWithinAWeekAreMarkedEndsSoon()
    {
        using var factory = new TestWebApplicationFactory();
        await AddAsync(factory, "Last chance offer", promotion: true, endsInDays: 3);
        await AddAsync(factory, "Long offer", promotion: true, endsInDays: 30);
        using var client = factory.CreateSecureClient();
        string html = await client.GetStringAsync("/Announcements");
        int soon = html.IndexOf("Ends soon");
        Assert.True(soon > 0 && soon == html.LastIndexOf("Ends soon"), "Only one post should be marked.");
        Assert.True(html.IndexOf("Last chance offer") > soon, "The badge should sit on the post that ends soon.");
    }

    private static async Task AddAsync(TestWebApplicationFactory factory, string title, bool promotion, bool published = true, int endsInDays = 30)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        DateTime now = DateTime.UtcNow;
        context.Announcements.Add(new Announcement
        {
            Title = title, Message = "Made for a test", IsPromotion = promotion, IsPublished = published,
            StartUtc = now.AddDays(endsInDays - 40), EndUtc = now.AddDays(endsInDays), UpdatedAtUtc = now
        });
        await context.SaveChangesAsync();
    }
}
