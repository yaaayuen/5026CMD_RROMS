using System.ComponentModel.DataAnnotations;
namespace Rroms.Web.Models;

/// <summary>
/// One chatbot question, recorded as its topic only (Alex). The question text and the asker are never stored,
/// so the statistics hold no personal data.
/// </summary>
public class ChatQuestionLog
{
    public int Id { get; set; }
    /// <summary>The matched topic, such as "menu" or "booking", or "other" when the question went to the AI or the fallback.</summary>
    [Required, StringLength(30)]
    public string Topic { get; set; } = "other";
    /// <summary>"Local", "AI" or "Fallback".</summary>
    [Required, StringLength(10)]
    public string Source { get; set; } = "Local";
    public DateTime AskedAtUtc { get; set; }
}
