using System.Text.RegularExpressions;

using Rroms.Tests.Infrastructure;

namespace Rroms.Tests.Theme;

/// <summary>
/// Gilded redesign: the landing page photos have alt text, so screen readers (including ones that read what is under
/// the mouse) describe the food instead of reading a file name. Decorative pictures must be marked alt="".
/// </summary>
public class HomePageAltTextTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public HomePageAltTextTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Every image on the home page has an alt attribute.</summary>
    [Fact]
    public async Task HomePage_EveryImageHasAnAltAttribute()
    {
        string html = await _factory.CreateSecureClient().GetStringAsync("/");

        MatchCollection images = Regex.Matches(html, "<img\\b[^>]*>", RegexOptions.IgnoreCase);

        Assert.NotEmpty(images);
        Assert.All(images, image => Assert.Matches("\\balt=\"", image.Value));
    }

    /// <summary>The four food photos (hero and three signature plates) describe the dish.</summary>
    [Fact]
    public async Task HomePage_FoodPhotosDescribeTheDish()
    {
        string html = await _factory.CreateSecureClient().GetStringAsync("/");

        MatchCollection described = Regex.Matches(html, "<img\\b[^>]*\\balt=\"([^\"]{20,})\"", RegexOptions.IgnoreCase);

        Assert.True(described.Count >= 4, $"Expected at least 4 described food photos, found {described.Count}.");
        Assert.Contains(described, match => match.Groups[1].Value.Contains("steak", StringComparison.OrdinalIgnoreCase));
    }
}
