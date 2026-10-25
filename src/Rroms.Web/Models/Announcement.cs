using System.ComponentModel.DataAnnotations;
namespace Rroms.Web.Models;

/// <summary>An announcement or promotional campaign, owned by Alex.</summary>
public class Announcement
{
    public int Id { get; set; }
    [Required, StringLength(120)]
    public string Title { get; set; } = "";
    [Required, StringLength(2000)]
    public string Message { get; set; } = "";
    public bool IsPromotion { get; set; }
    public bool IsPublished { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    /// <summary>When customers were told about this promotion. Empty until its start time has arrived.</summary>
    public DateTime? NotifiedAtUtc { get; set; }
    /// <summary>Optional dish the post is about, so customers can open it on the menu.</summary>
    public int? MenuItemId { get; set; }
    public MenuItem? MenuItem { get; set; }
    /// <summary>Days the post repeats on, as a <see cref="Services.DiscountDays"/> mask. 0 means it is not recurring.</summary>
    public int RepeatDays { get; set; }
    /// <summary>Daily start and end in Malaysia time for a recurring post. Empty means all day.</summary>
    public TimeOnly? DailyFromLocal { get; set; }
    public TimeOnly? DailyUntilLocal { get; set; }
}
