using System.Text.Json.Serialization;
namespace Rroms.Web.Models.ViewModels;

/// <summary>A text answer, optional how-to steps and an optional server-selected customer page.</summary>
public class ChatReply
{
    public string Answer { get; set; } = "";
    public string Source { get; set; } = "Local";
    public string? PageUrl { get; set; }
    public string? PageLabel { get; set; }
    public IReadOnlyList<string> Steps { get; set; } = Array.Empty<string>();
    /// <summary>The matched topic, kept only for the admin statistics and never sent to the browser.</summary>
    [JsonIgnore]
    public string Topic { get; set; } = "other";
}
