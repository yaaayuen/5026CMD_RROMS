namespace Rroms.Web.Models.ViewModels;

/// <summary>Which reviews the admin feedback list shows.</summary>
public enum FeedbackView
{
    All,
    Shown,
    Hidden,
    Flagged,
    AwaitingReply,
    Replied
}

/// <summary>All-time rating totals and the recent weekly trend for the admin feedback page.</summary>
public class FeedbackSummary
{
    public int Count { get; set; }
    public double? Average { get; set; }
    /// <summary>Number of reviews for 1 to 5 stars; index 0 is one star.</summary>
    public int[] StarCounts { get; set; } = new int[5];
    public int HiddenCount { get; set; }
    public int FlaggedCount { get; set; }
    public int AwaitingReplyCount { get; set; }
    /// <summary>Oldest week first, each starting on a Monday in Malaysia time.</summary>
    public List<FeedbackWeek> Weeks { get; set; } = new List<FeedbackWeek>();
}

/// <summary>One week of the rating trend. Average is empty when nobody left feedback that week.</summary>
public record FeedbackWeek(DateTime StartLocal, int Count, double? Average);

/// <summary>One dish's average stars from guests who rated it.</summary>
public record DishRatingSummary(int MenuItemId, string Name, int Count, double Average);
