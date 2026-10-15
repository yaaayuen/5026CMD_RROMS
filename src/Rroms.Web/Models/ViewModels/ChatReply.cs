namespace Rroms.Web.Models.ViewModels;

/// <summary>A text answer and an optional server-selected customer page.</summary>
public class ChatReply
{
    public string Answer { get; set; } = "";
    public string Source { get; set; } = "Local";
    public string? PageUrl { get; set; }
    public string? PageLabel { get; set; }
}
