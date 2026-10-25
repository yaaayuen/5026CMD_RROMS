using Rroms.Web.Common;
using Rroms.Web.Models;
namespace Rroms.Web.Services;

/// <summary>
/// When an announcement is on the website. A one-off post shows from its start to its end; a recurring post
/// shows only on its chosen days (and daily hours, in Malaysia time) between those dates.
/// </summary>
public static class AnnouncementSchedule
{
    /// <summary>True when the post repeats on chosen days.</summary>
    public static bool IsRecurring(Announcement item) => item.RepeatDays != 0;

    /// <summary>When the showing that contains <paramref name="nowUtc"/> began, or null when the post is not showing.</summary>
    public static DateTime? CurrentStartUtc(Announcement item, DateTime nowUtc)
    {
        if (!item.IsPublished || nowUtc < item.StartUtc || nowUtc >= item.EndUtc) { return null; }
        if (!IsRecurring(item)) { return item.StartUtc; }
        DateTime local = MalaysiaTime.FromUtc(nowUtc);
        if (!DiscountDays.Includes(item.RepeatDays, local.DayOfWeek)) { return null; }
        TimeOnly time = TimeOnly.FromDateTime(local);
        TimeOnly from = item.DailyFromLocal ?? TimeOnly.MinValue;
        if (time < from || (item.DailyUntilLocal.HasValue && time >= item.DailyUntilLocal.Value)) { return null; }
        // The first day's showing starts no earlier than the post itself.
        DateTime startUtc = MalaysiaTime.ToUtc(local.Date + from.ToTimeSpan());
        return startUtc < item.StartUtc ? item.StartUtc : startUtc;
    }

    /// <summary>True when customers can see the post right now.</summary>
    public static bool IsShowing(Announcement item, DateTime nowUtc) => CurrentStartUtc(item, nowUtc).HasValue;

    /// <summary>The next time the post starts showing after <paramref name="nowUtc"/>, or null when it never shows again.</summary>
    public static DateTime? NextStartUtc(Announcement item, DateTime nowUtc)
    {
        if (nowUtc >= item.EndUtc) { return null; }
        if (!IsRecurring(item)) { return item.StartUtc > nowUtc ? item.StartUtc : null; }
        DateTime after = nowUtc < item.StartUtc ? item.StartUtc : nowUtc;
        DateTime firstDay = MalaysiaTime.FromUtc(after).Date;
        TimeOnly from = item.DailyFromLocal ?? TimeOnly.MinValue;
        // A week covers every chosen day; the eighth day catches today's slot when it has already passed.
        for (int day = 0; day <= 7; day++)
        {
            DateTime date = firstDay.AddDays(day);
            if (!DiscountDays.Includes(item.RepeatDays, date.DayOfWeek)) { continue; }
            DateTime startUtc = MalaysiaTime.ToUtc(date + from.ToTimeSpan());
            DateTime endUtc = MalaysiaTime.ToUtc(item.DailyUntilLocal.HasValue ? date + item.DailyUntilLocal.Value.ToTimeSpan() : date.AddDays(1));
            if (endUtc <= after || startUtc >= item.EndUtc) { continue; }
            DateTime begins = startUtc < item.StartUtc ? item.StartUtc : startUtc;
            if (begins > nowUtc) { return begins; }
        }
        return null;
    }

    /// <summary>Readable repeat rule, for example "Mon–Fri, 11:00 AM – 2:00 PM". Empty for a one-off post.</summary>
    public static string Describe(Announcement item)
    {
        if (!IsRecurring(item)) { return ""; }
        string days = DiscountDays.Describe(item.RepeatDays);
        if (item.DailyFromLocal == null && item.DailyUntilLocal == null) { return days + ", all day"; }
        string from = (item.DailyFromLocal ?? TimeOnly.MinValue).ToString("h:mm tt");
        string until = item.DailyUntilLocal?.ToString("h:mm tt") ?? "midnight";
        return $"{days}, {from} – {until}";
    }
}
