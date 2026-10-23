using System.Net;

using Rroms.Tests.Infrastructure;

namespace Rroms.Tests.Menu;

/// <summary>
/// YJ-2 acceptance: a visitor who is not signed in can find the menu from the top bar of any page,
/// not only by typing /Menu. Owner: YJ.
/// </summary>
public class MenuNavigationTests : IClassFixture<TestWebApplicationFactory>
{
    private const string MenuLink = "href=\"/Menu\"";

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public MenuNavigationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The home page top bar has a Menu link for a visitor.</summary>
    [Fact]
    public async Task Visitor_SeesAMenuLinkInTheTopBarOnTheHomePage()
    {
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await client.GetAsync("/");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(MenuLink, html);
    }

    /// <summary>The link is in the shared layout, so other public pages show it too.</summary>
    [Fact]
    public async Task Visitor_SeesAMenuLinkInTheTopBarOnTheNewsPage()
    {
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await client.GetAsync("/Announcements");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(MenuLink, html);
    }

    /// <summary>On the menu page itself the link is the highlighted (active) one.</summary>
    [Fact]
    public async Task MenuPage_HighlightsTheMenuLink()
    {
        HttpClient client = _factory.CreateSecureClient();

        string html = await client.GetStringAsync("/Menu");

        Assert.Contains("nav-link active", html);
        Assert.Contains("aria-current=\"page\"", html);
    }
}
