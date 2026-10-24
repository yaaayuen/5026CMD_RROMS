using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rroms.Web.Common;
using Rroms.Web.Models.ViewModels;
using Rroms.Web.Services;
namespace Rroms.Web.Areas.Admin.Pages.Feedback;

/// <summary>Rating summary, filters and moderation: reply, hide from the website, delete a comment and manage blocked words.</summary>
public class IndexModel : PageModel
{
    /// <summary>Weeks shown in the rating trend.</summary>
    public const int TrendWeeks = 8;
    private readonly IFeedbackService _service;
    public IndexModel(IFeedbackService service) { _service = service; }
    [BindProperty(SupportsGet = true)] public int? Rating { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? Until { get; set; }
    [BindProperty(SupportsGet = true)] public FeedbackView View { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    public List<Models.Feedback> Items { get; set; } = new List<Models.Feedback>();
    public FeedbackSummary Summary { get; set; } = new FeedbackSummary();
    public double? Average => Items.Count == 0 ? null : Items.Average(f => f.Rating);
    public List<Models.BlockedWord> CustomWords { get; set; } = new List<Models.BlockedWord>();
    public IReadOnlyList<string> FixedWords => _service.FixedBlockedWords;
    public IReadOnlyList<string> BlockedWordsIn(string? comment) => _service.BlockedWordsIn(comment, CustomWords.Select(w => w.Word));

    /// <summary>The current filters, so a moderation action returns to the same list.</summary>
    public Dictionary<string, string?> Filters => new Dictionary<string, string?>
    {
        ["Rating"] = Rating?.ToString(), ["From"] = From?.ToString("yyyy-MM-dd"), ["Until"] = Until?.ToString("yyyy-MM-dd"),
        ["View"] = View == FeedbackView.All ? null : View.ToString(), ["Search"] = Search
    };

    public async Task OnGetAsync()
    {
        Summary = await _service.GetSummaryAsync(TrendWeeks);
        CustomWords = await _service.GetCustomBlockedWordsAsync();
        if (!ModelState.IsValid) { return; }
        if (From > Until) { ModelState.AddModelError("", "End date must not precede start date."); return; }
        Items = await _service.GetAsync(Rating, From.HasValue ? MalaysiaTime.ToUtc(From.Value.Date) : null,
            Until.HasValue ? MalaysiaTime.ToUtc(Until.Value.Date.AddDays(1)) : null, View, Search);
    }

    public Task<IActionResult> OnPostReplyAsync(int id, string? reply) =>
        RunAsync(_service.ReplyAsync(id, reply), "Reply saved. The customer has been notified.");

    public Task<IActionResult> OnPostHideAsync(int id, bool hidden) =>
        RunAsync(_service.SetHiddenAsync(id, hidden), hidden ? "Review hidden from the website." : "Review shown on the website again.");

    public Task<IActionResult> OnPostRemoveCommentAsync(int id) =>
        RunAsync(_service.RemoveCommentAsync(id), "Comment deleted. The star rating is kept.");

    public Task<IActionResult> OnPostAddWordAsync(string? word) =>
        RunAsync(_service.AddBlockedWordAsync(word), "Word added. New reviews containing it are hidden automatically.");

    public Task<IActionResult> OnPostRemoveWordAsync(int wordId) =>
        RunAsync(_service.RemoveBlockedWordAsync(wordId), "Word removed from the filter.");

    public async Task<IActionResult> OnPostRecheckAsync()
    {
        int flagged = await _service.RecheckAsync();
        TempData["StatusMessage"] = flagged == 0
            ? "Re-check finished. No other reviews contain a blocked word."
            : $"Re-check finished. {flagged} {(flagged == 1 ? "review was" : "reviews were")} hidden and flagged.";
        return RedirectToPage(Filters.Where(f => f.Value != null).ToDictionary(f => f.Key, f => (object?)f.Value));
    }

    private async Task<IActionResult> RunAsync(Task<ServiceResult> action, string success)
    {
        ServiceResult result = await action;
        TempData["StatusMessage"] = result.Succeeded ? success : string.Join(" ", result.Errors);
        return RedirectToPage(Filters.Where(f => f.Value != null).ToDictionary(f => f.Key, f => (object?)f.Value));
    }
}
