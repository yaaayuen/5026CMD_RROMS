using System.Net;

using Rroms.Tests.Infrastructure;

namespace Rroms.Tests.Theme;

/// <summary>
/// YJ-6 acceptance for the page frame (_Layout): the theme button, the theme script, the skip link and the
/// language attribute are on every page. Owner: YJ.
/// </summary>
/// <remarks>
/// Written BEFORE the layout edit (test-first): the checks that need the new layout markup fail (red) and turn green
/// when _Layout.cshtml is updated. They read the public home page, which uses the same layout as every other page.
/// </remarks>
public class ThemeLayoutTests : IClassFixture<TestWebApplicationFactory>
{
    private const string HomeUrl = "/";

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public ThemeLayoutTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The theme script is served.</summary>
    [Fact]
    public async Task ThemeScript_IsServed()
    {
        HttpClient client = _factory.CreateSecureClient();

        HttpResponseMessage response = await client.GetAsync("/js/theme.js");
        string script = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-bs-theme", script);
        Assert.Contains("prefers-color-scheme", script);
    }

    /// <summary>The layout loads the theme script.</summary>
    [Fact]
    public async Task Layout_LoadsTheThemeScript()
    {
        HttpClient client = _factory.CreateSecureClient();

        string html = await client.GetStringAsync(HomeUrl);

        Assert.Contains("/js/theme.js", html);
    }

    /// <summary>The layout has a theme button that tells assistive technology its state.</summary>
    [Fact]
    public async Task Layout_HasAThemeToggleButton()
    {
        HttpClient client = _factory.CreateSecureClient();

        string html = await client.GetStringAsync(HomeUrl);

        Assert.Contains("data-theme-toggle", html);
        Assert.Contains("aria-pressed=\"false\"", html);
        Assert.Contains(">Dark mode<", html);
    }

    /// <summary>The first thing a keyboard user can reach is a link that skips to the main content, and it has a target.</summary>
    [Fact]
    public async Task Layout_HasASkipLinkWithATarget()
    {
        HttpClient client = _factory.CreateSecureClient();

        string html = await client.GetStringAsync(HomeUrl);

        Assert.Contains("class=\"skip-link\"", html);
        Assert.Contains("href=\"#main-content\"", html);
        Assert.Contains("id=\"main-content\"", html);
    }

    /// <summary>The page declares its language, so screen readers pronounce it correctly.</summary>
    [Fact]
    public async Task Layout_DeclaresThePageLanguage()
    {
        HttpClient client = _factory.CreateSecureClient();

        string html = await client.GetStringAsync(HomeUrl);

        Assert.Contains("<html lang=\"en\"", html);
    }
}
