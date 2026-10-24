using Rroms.Web.Common;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;

/// <summary>Completed visit feedback, owned by Alex.</summary>
public interface IFeedbackService
{
    Task<List<Reservation>> GetEligibleAsync(string customerId);
    Task<ServiceResult> SubmitAsync(string customerId, int reservationId, int rating, string? comment);
    Task<List<Feedback>> GetAsync(int? rating, DateTime? fromUtc, DateTime? untilUtc,
        FeedbackView view = FeedbackView.All, string? search = null);
    /// <summary>The customer's own reviews with any reply, newest first.</summary>
    Task<List<Feedback>> GetMineAsync(string customerId);
    /// <summary>Newest reviews that may appear on the website: not hidden and with a comment.</summary>
    Task<List<Feedback>> GetPublicAsync(int take);
    Task<FeedbackSummary> GetSummaryAsync(int weeks);
    /// <summary>Saves the restaurant's reply. The customer is notified the first time only.</summary>
    Task<ServiceResult> ReplyAsync(int id, string? reply);
    Task<ServiceResult> SetHiddenAsync(int id, bool hidden);
    /// <summary>Blocked words in a comment, so the admin page can say why a review was hidden.</summary>
    IReadOnlyList<string> BlockedWordsIn(string? comment, IEnumerable<string> customWords);
    /// <summary>Built-in and settings words, which administrators cannot remove.</summary>
    IReadOnlyList<string> FixedBlockedWords { get; }
    /// <summary>Words administrators added, alphabetical.</summary>
    Task<List<BlockedWord>> GetCustomBlockedWordsAsync();
    Task<ServiceResult> AddBlockedWordAsync(string? word);
    Task<ServiceResult> RemoveBlockedWordAsync(int id);
    /// <summary>Hides and flags older reviews that contain a blocked word. Returns how many were newly flagged.</summary>
    Task<int> RecheckAsync();
    /// <summary>Permanently deletes the comment text but keeps the rating, so the visit cannot be reviewed again.</summary>
    Task<ServiceResult> RemoveCommentAsync(int id);
}
