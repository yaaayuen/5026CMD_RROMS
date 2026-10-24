using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Rroms.Web.Options;
namespace Rroms.Web.Services;

/// <summary>
/// Finds blocked words in a review comment (Alex). A match hides the review until an administrator checks it.
/// </summary>
/// <remarks>
/// Text is lower-cased, common look-alike characters are mapped back to letters ("sh1t", "$hit") and
/// repeated letters are collapsed ("shiiit"), so simple disguises still match. Only whole words and a few
/// endings match, so "Dickens" or "Scunthorpe" are not flagged.
/// </remarks>
public class FeedbackWordFilter
{
    // English, Malay and local slang insults. Settings can add more under "Feedback:BlockedWords",
    // and administrators add their own on the feedback page.
    private static readonly string[] BuiltIn =
    {
        "fuck", "shit", "bitch", "bastard", "asshole", "dick", "cunt", "motherfucker", "wanker",
        "bodoh", "babi", "sial", "puki", "pukimak", "lancau", "celaka", "cibai", "kanina"
    };
    // One compiled pattern per word, shared by every request because the filter is a singleton.
    private readonly ConcurrentDictionary<string, Regex> _patterns = new ConcurrentDictionary<string, Regex>();

    public FeedbackWordFilter(IOptions<FeedbackOptions> options)
    {
        FixedWords = BuiltIn.Concat(options.Value.BlockedWords)
            .Select(w => w.Trim().ToLowerInvariant()).Where(w => w.Length > 0).Distinct().ToList();
    }

    /// <summary>The built-in words plus any from settings. Administrators cannot remove these.</summary>
    public IReadOnlyList<string> FixedWords { get; }

    /// <summary>The blocked words found in the text, in list order. Empty when the text is clean.</summary>
    public IReadOnlyList<string> Find(string? text, IEnumerable<string>? extraWords = null)
    {
        if (string.IsNullOrWhiteSpace(text)) { return Array.Empty<string>(); }
        string normal = Normalise(text);
        return FixedWords.Concat(extraWords ?? Array.Empty<string>()).Distinct()
            .Where(w => _patterns.GetOrAdd(w, Build).IsMatch(normal)).ToList();
    }

    private static Regex Build(string word) =>
        new Regex($"(?<![a-z]){Regex.Escape(Normalise(word))}(?:s|es|ed|er|ers|in|ing|y)?(?![a-z])",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static string Normalise(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text.ToLowerInvariant())
        {
            builder.Append(c switch { '0' => 'o', '1' => 'i', '3' => 'e', '4' or '@' => 'a', '5' or '$' => 's', '7' => 't', _ => c });
        }
        // "shiiit" and "fuuuck" become "shit" and "fuck"; the words are collapsed the same way, so "asshole" still matches.
        return Regex.Replace(builder.ToString(), @"([a-z])\1+", "$1");
    }
}
