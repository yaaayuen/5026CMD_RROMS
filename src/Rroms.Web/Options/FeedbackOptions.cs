namespace Rroms.Web.Options;

/// <summary>Extra words that hide a new review automatically, on top of the built-in list in FeedbackWordFilter.</summary>
public class FeedbackOptions
{
    public const string SectionName = "Feedback";
    public string[] BlockedWords { get; set; } = Array.Empty<string>();
}
