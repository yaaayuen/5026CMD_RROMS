using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;

/// <summary>Read-only statistics for administrators: kitchen performance and chatbot use (Alex).</summary>
public interface IKitchenInsightsService
{
    /// <summary>Kitchen numbers for one Malaysia day, with the seven days ending on it.</summary>
    Task<KitchenStats> GetKitchenStatsAsync(DateOnly day);
    /// <summary>Chatbot questions over the last <paramref name="days"/> days, by topic and answer source.</summary>
    Task<ChatInsights> GetChatInsightsAsync(int days);
    /// <summary>Records one chatbot question by topic and source only. Never throws: statistics must not break the chat.</summary>
    Task RecordQuestionAsync(string topic, string source);
}
