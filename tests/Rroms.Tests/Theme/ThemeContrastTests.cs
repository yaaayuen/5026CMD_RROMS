using System.Globalization;
using System.Text.RegularExpressions;

using Rroms.Tests.Infrastructure;

namespace Rroms.Tests.Theme;

/// <summary>
/// YJ-6 acceptance: the colour pairs in wwwroot/css/site.css meet WCAG AA contrast in BOTH themes. Owner: YJ.
/// </summary>
/// <remarks>
/// The test downloads the real stylesheet from the running app and reads the colour tokens out of it, so it fails
/// the moment someone changes a colour to something unreadable. Text needs a ratio of at least 4.5, and the
/// keyboard focus outline (not text) needs at least 3.0 (WCAG 2.2 non-text contrast).
/// The light tokens are in the :root blocks; the dark theme replaces some of them in the [data-bs-theme="dark"] block.
/// </remarks>
public class ThemeContrastTests : IClassFixture<TestWebApplicationFactory>
{
    private const double TextMinimum = 4.5;
    private const double FocusMinimum = 3.0;

    private readonly TestWebApplicationFactory _factory;

    /// <summary>Receives the shared in-memory application from xUnit.</summary>
    /// <param name="factory">The test application.</param>
    public ThemeContrastTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Every text pair that appears on screen, as: theme, text token, background token, minimum ratio.</summary>
    public static IEnumerable<object[]> Pairs()
    {
        foreach (string theme in new[] { "light", "dark" })
        {
            yield return new object[] { theme, "tg-ink", "tg-cream", TextMinimum };
            yield return new object[] { theme, "tg-ink", "tg-surface", TextMinimum };
            yield return new object[] { theme, "tg-muted", "tg-cream", TextMinimum };
            yield return new object[] { theme, "tg-muted", "tg-surface", TextMinimum };
            yield return new object[] { theme, "tg-link", "tg-cream", TextMinimum };
            yield return new object[] { theme, "tg-link", "tg-surface", TextMinimum };
            yield return new object[] { theme, "tg-link-hover", "tg-blush", TextMinimum };
            yield return new object[] { theme, "tg-ink", "tg-blush", TextMinimum };
            yield return new object[] { theme, "tg-on-brand", "tg-raspberry", TextMinimum };
            yield return new object[] { theme, "tg-on-brand", "tg-raspberry-dark", TextMinimum };
            yield return new object[] { theme, "tg-on-gold", "tg-gold", TextMinimum };
            yield return new object[] { theme, "tg-focus", "tg-cream", FocusMinimum };
            yield return new object[] { theme, "tg-focus", "tg-surface", FocusMinimum };
        }
    }

    /// <summary>Each colour pair is readable in its theme.</summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task ColourPair_MeetsTheMinimumContrast(string theme, string textToken, string backgroundToken, double minimum)
    {
        Dictionary<string, string> tokens = await LoadTokensAsync(theme);

        double ratio = Contrast(tokens[textToken], tokens[backgroundToken]);

        Assert.True(
            ratio >= minimum,
            $"{theme}: {textToken} {tokens[textToken]} on {backgroundToken} {tokens[backgroundToken]} is {ratio:F2}:1, needs {minimum}:1.");
    }

    // ============================== Reading the stylesheet ==============================//

    /// <summary>Downloads site.css and returns the colour tokens in force for a theme.</summary>
    private async Task<Dictionary<string, string>> LoadTokensAsync(string theme)
    {
        HttpClient client = _factory.CreateSecureClient();
        string css = await client.GetStringAsync("/css/site.css");

        var tokens = new Dictionary<string, string>();

        // Light tokens: every ":root { ... }" block.
        foreach (Match block in Regex.Matches(css, @":root\s*\{([^}]*)\}"))
        {
            ReadTokens(block.Groups[1].Value, tokens);
        }

        // Dark theme: the one block whose selector is exactly [data-bs-theme="dark"] { ... } replaces some tokens.
        if (theme == "dark")
        {
            Match dark = Regex.Match(css, "\\[data-bs-theme=\"dark\"\\]\\s*\\{([^}]*)\\}");
            Assert.True(dark.Success, "site.css has no [data-bs-theme=\"dark\"] token block.");
            ReadTokens(dark.Groups[1].Value, tokens);
        }

        return tokens;
    }

    /// <summary>Copies every "--name: #rrggbb;" line of a block into the dictionary (later values win).</summary>
    private static void ReadTokens(string block, Dictionary<string, string> tokens)
    {
        foreach (Match line in Regex.Matches(block, @"--([a-z0-9-]+)\s*:\s*(#[0-9a-fA-F]{6})\s*;"))
        {
            tokens[line.Groups[1].Value] = line.Groups[2].Value;
        }
    }

    // ============================== WCAG maths ==============================//

    /// <summary>The contrast ratio between two #rrggbb colours: from 1 (identical) to 21 (black on white).</summary>
    private static double Contrast(string firstHex, string secondHex)
    {
        double first = Luminance(firstHex);
        double second = Luminance(secondHex);

        double lighter = Math.Max(first, second);
        double darker = Math.Min(first, second);

        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>The relative luminance of a colour as defined by WCAG.</summary>
    private static double Luminance(string hex)
    {
        double red = Channel(hex.Substring(1, 2));
        double green = Channel(hex.Substring(3, 2));
        double blue = Channel(hex.Substring(5, 2));

        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static double Channel(string twoHexDigits)
    {
        double value = int.Parse(twoHexDigits, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;

        if (value <= 0.03928)
        {
            return value / 12.92;
        }

        return Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
