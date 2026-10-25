using Microsoft.EntityFrameworkCore;
using Rroms.Web.Common;
using Rroms.Web.Data;
using Rroms.Web.Models;
using Rroms.Web.Models.ViewModels;
namespace Rroms.Web.Services;

/// <summary>Builds the kitchen and chatbot statistics from KitchenTickets and ChatQuestionLogs.</summary>
public class KitchenInsightsService : IKitchenInsightsService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<KitchenInsightsService> _logger;
    public KitchenInsightsService(ApplicationDbContext context, ILogger<KitchenInsightsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<KitchenStats> GetKitchenStatsAsync(DateOnly day)
    {
        DateTime dayStart = MalaysiaTime.ToUtc(day.ToDateTime(TimeOnly.MinValue));
        DateTime weekStart = dayStart.AddDays(-6);
        DateTime dayEnd = dayStart.AddDays(1);
        // A week of tickets is small, so the grouping is done in memory in Malaysia time.
        var tickets = await _context.KitchenTickets.AsNoTracking()
            .Where(t => t.StartedAtUtc >= weekStart && t.StartedAtUtc < dayEnd).ToListAsync();
        var today = tickets.Where(t => t.StartedAtUtc >= dayStart).ToList();
        var cooked = today.Where(t => t.CookingMinutes > 0).ToList();
        var served = today.Where(t => t.ServedAtUtc.HasValue).ToList();

        var stats = new KitchenStats
        {
            Day = day,
            Orders = today.Count,
            DrinksOnly = today.Count - cooked.Count,
            AverageWaitMinutes = cooked.Count == 0 ? null : cooked.Average(Wait),
            AverageCookMinutes = cooked.Count == 0 ? null : cooked.Average(t => (t.ReadyAtUtc - t.StartedAtUtc).TotalMinutes),
            AverageReadyToServedMinutes = served.Count == 0 ? null
                : served.Average(t => Math.Max(0, (t.ServedAtUtc!.Value - t.ReadyAtUtc).TotalMinutes))
        };
        foreach (var ticket in today) { stats.OrdersByHour[MalaysiaTime.FromUtc(ticket.StartedAtUtc).Hour]++; }
        if (today.Count > 0) { stats.BusiestHour = Array.IndexOf(stats.OrdersByHour, stats.OrdersByHour.Max()); }
        for (int n = 6; n >= 0; n--)
        {
            DateOnly date = day.AddDays(-n);
            var onDay = tickets.Where(t => DateOnly.FromDateTime(MalaysiaTime.FromUtc(t.StartedAtUtc)) == date).ToList();
            var onDayCooked = onDay.Where(t => t.CookingMinutes > 0).ToList();
            stats.Week.Add(new KitchenDay(date, onDay.Count, onDayCooked.Count == 0 ? null : onDayCooked.Average(Wait)));
        }
        return stats;
    }

    public async Task<ChatInsights> GetChatInsightsAsync(int days)
    {
        days = Math.Clamp(days, 1, 90);
        DateOnly today = DateOnly.FromDateTime(MalaysiaTime.FromUtc(DateTime.UtcNow));
        DateTime fromUtc = MalaysiaTime.ToUtc(today.AddDays(1 - days).ToDateTime(TimeOnly.MinValue));
        var logs = await _context.ChatQuestionLogs.AsNoTracking().Where(l => l.AskedAtUtc >= fromUtc).ToListAsync();
        var insights = new ChatInsights
        {
            Days = days,
            Total = logs.Count,
            Local = logs.Count(l => l.Source == "Local"),
            Ai = logs.Count(l => l.Source == "AI"),
            Fallback = logs.Count(l => l.Source == "Fallback"),
            Topics = logs.GroupBy(l => l.Topic).Select(g => (g.Key, g.Count()))
                .OrderByDescending(t => t.Item2).ThenBy(t => t.Key).ToList()
        };
        for (int n = days - 1; n >= 0; n--)
        {
            DateOnly date = today.AddDays(-n);
            insights.PerDay.Add((date, logs.Count(l => DateOnly.FromDateTime(MalaysiaTime.FromUtc(l.AskedAtUtc)) == date)));
        }
        return insights;
    }

    public async Task RecordQuestionAsync(string topic, string source)
    {
        try
        {
            _context.ChatQuestionLogs.Add(new ChatQuestionLog { Topic = topic.Length > 30 ? topic[..30] : topic, Source = source, AskedAtUtc = DateTime.UtcNow });
            await _context.SaveChangesAsync();
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException)
        {
            // The answer has already been worked out; losing one statistic is better than failing the chat.
            _logger.LogWarning(exception, "Could not record a chatbot question topic.");
        }
    }

    private static double Wait(KitchenTicket ticket) => Math.Max(0, (ticket.StartedAtUtc - ticket.QueuedSinceUtc).TotalMinutes);
}
